using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickTranslate.Services;

internal static class TesseractEnglishOcr
{
    private const string Library = "QuickTranslate.Tesseract.dll";
    private const string ModelResource = "QuickTranslate.Assets.Ocr.eng.traineddata";
    private const CallingConvention Cdecl = CallingConvention.Cdecl;
    private const int LstmOnly = 1;
    private const int SparseText = 11;
    private static readonly Lazy<byte[]> Model = new(LoadModel);

    internal readonly record struct Reading(string Text, int Confidence);

    public static Reading? TryRecognize(Bitmap bitmap, CancellationToken token)
    {
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return null;
        token.ThrowIfCancellationRequested();

        try
        {
            return Recognize(bitmap, token);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException
            or EntryPointNotFoundException or IOException or InvalidOperationException)
        {
            LogService.Warn("内置英文 OCR 暂不可用", ex);
            return null;
        }
    }

    private static Reading Recognize(Bitmap bitmap, CancellationToken token)
    {
        nint engine = TessBaseAPICreate();
        if (engine == 0) throw new InvalidOperationException("无法创建 Tesseract 引擎。");

        try
        {
            byte[] model = Model.Value;
            if (TessBaseAPIInit5(engine, model, model.Length, "eng", LstmOnly, 0, 0, 0, 0, 0, 0) != 0)
                throw new InvalidOperationException("无法加载内置英文 OCR 模型。");

            TessBaseAPISetPageSegMode(engine, SparseText);
            byte[] pixels = CopyGrayscalePixels(bitmap, token);
            TessBaseAPISetImage(engine, pixels, bitmap.Width, bitmap.Height, 1, bitmap.Width);
            TessBaseAPISetSourceResolution(engine, 300);

            nint monitor = TessMonitorCreate();
            if (monitor == 0) throw new InvalidOperationException("无法创建 OCR 取消监视器。");
            try
            {
                CancelCallback cancelled = (_, _) => token.IsCancellationRequested;
                TessMonitorSetCancelFunc(monitor, cancelled);
                int status = TessBaseAPIRecognize(engine, monitor);
                GC.KeepAlive(cancelled);
                token.ThrowIfCancellationRequested();
                if (status != 0) throw new InvalidOperationException("Tesseract 识别失败。");
            }
            finally
            {
                TessMonitorDelete(monitor);
            }

            nint text = TessBaseAPIGetUTF8Text(engine);
            if (text == 0) return new Reading(string.Empty, 0);
            try
            {
                return new Reading(Marshal.PtrToStringUTF8(text)?.Trim() ?? string.Empty,
                    TessBaseAPIMeanTextConf(engine));
            }
            finally
            {
                TessDeleteText(text);
            }
        }
        finally
        {
            TessBaseAPIDelete(engine);
        }
    }

    private static byte[] CopyGrayscalePixels(Bitmap bitmap, CancellationToken token)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        byte[] pixels = new byte[checked(width * height)];
        byte[] row = new byte[checked(width * 4)];
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (int x = 0; x < width; x++)
                {
                    int pixel = x * 4;
                    pixels[y * width + x] = (byte)((29 * row[pixel] + 150 * row[pixel + 1] + 77 * row[pixel + 2]) >> 8);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return pixels;
    }

    private static byte[] LoadModel()
    {
        using var stream = typeof(TesseractEnglishOcr).Assembly.GetManifestResourceStream(ModelResource)
            ?? throw new FileNotFoundException("找不到内置英文 OCR 模型。");
        byte[] model = new byte[checked((int)stream.Length)];
        stream.ReadExactly(model);
        return model;
    }

    [UnmanagedFunctionPointer(Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool CancelCallback(nint state, int words);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern nint TessBaseAPICreate();

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessBaseAPIDelete(nint engine);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern int TessBaseAPIInit5(nint engine, byte[] model, int size,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string language, int mode, nint configs, int configCount,
        nint variableNames, nint variableValues, nuint variableCount, int setOnlyNonDebugParams);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessBaseAPISetPageSegMode(nint engine, int mode);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessBaseAPISetImage(nint engine, byte[] pixels, int width, int height,
        int bytesPerPixel, int bytesPerLine);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessBaseAPISetSourceResolution(nint engine, int dpi);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern nint TessMonitorCreate();

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessMonitorDelete(nint monitor);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessMonitorSetCancelFunc(nint monitor, CancelCallback callback);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern int TessBaseAPIRecognize(nint engine, nint monitor);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern nint TessBaseAPIGetUTF8Text(nint engine);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern int TessBaseAPIMeanTextConf(nint engine);

    [DllImport(Library, CallingConvention = Cdecl, ExactSpelling = true)]
    private static extern void TessDeleteText(nint text);
}
