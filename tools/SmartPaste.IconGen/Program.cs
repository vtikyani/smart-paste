using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartPaste.App.UI.Brand;

namespace SmartPaste.IconGen;

// Writes the Smart Paste app icon and logo images, all rendered from the vector mark in BrandArt:
//   src/SmartPaste.App/Assets/SmartPaste.ico   16 to 256 px, each size hinted to whole pixels
//   docs/images/logo.png                       512 px
//   docs/images/logo-mark.svg                  the mark as SVG
// Usage: dotnet run --project tools/SmartPaste.IconGen [repo root]
internal static class Program
{
    // Every size Windows asks an app icon for, from the small taskbar and title-bar icons up to the 256 px tile.
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [STAThread]
    private static int Main(string[] args)
    {
        var root = args.Length > 0 ? Path.GetFullPath(args[0]) : FindRepoRoot();
        if (root is null)
        {
            Console.Error.WriteLine("Run this from inside the repository, or pass the repository root.");
            return 1;
        }

        var icon = Path.Combine(root, "src", "SmartPaste.App", "Assets", "SmartPaste.ico");
        var images = Path.Combine(root, "docs", "images");
        Directory.CreateDirectory(Path.GetDirectoryName(icon)!);
        Directory.CreateDirectory(images);

        IconFile.Write(icon, IconSizes.Select(size => Render(size)).ToArray());
        Write(Render(512), Path.Combine(images, "logo.png"));
        File.WriteAllText(Path.Combine(images, "logo-mark.svg"), BrandArt.ToSvg());

        Console.WriteLine($"Wrote {Path.GetRelativePath(root, icon)} ({string.Join(", ", IconSizes)} px)");
        Console.WriteLine($"Wrote {Path.GetRelativePath(root, Path.Combine(images, "logo.png"))} (512 px)");
        Console.WriteLine($"Wrote {Path.GetRelativePath(root, Path.Combine(images, "logo-mark.svg"))}");
        return 0;
    }

    private static BitmapSource Render(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawDrawing(BrandArt.CreateLogo(size));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Write(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SmartPaste.slnx"))) return dir.FullName;
        return null;
    }
}
