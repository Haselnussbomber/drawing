using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Data.Files;
using System.Linq;
using System.Reflection;
using TerraFX.Interop.DirectX;

namespace Una.Drawing.Texture;

internal static class TextureLoader
{
    private static readonly Dictionary<uint, (TexFile iconFile, string iconPath)>     IconToTexFileCache = [];
    private static readonly Dictionary<string, nint>                                  PathToBytePtrCache = [];
    private static readonly Dictionary<string, (byte[] bytes, int height, int width)> PathToByteArrayCache = [];
    private static readonly Dictionary<string, SKImage>                               PathToImageCache = [];
    private static readonly Dictionary<uint, SKImage>                                 IconToImageCache = [];
    private static readonly Dictionary<string, TexFile>                               PathToTexFileCache = [];
    private static readonly Dictionary<string, UldFile>                               PathToUldFileCache = [];

    internal static void Dispose()
    {
        foreach (var (_, image) in IconToImageCache) image.Dispose();
        foreach (var (_, image) in PathToImageCache) image.Dispose();
        foreach (var (_, image) in PathToBytePtrCache) Marshal.FreeHGlobal(image);

        IconToImageCache.Clear();
        IconToTexFileCache.Clear();
        PathToTexFileCache.Clear();
        PathToUldFileCache.Clear();
        PathToBytePtrCache.Clear();
        PathToByteArrayCache.Clear();
        PathToImageCache.Clear();
    }

    /// <summary>
    /// Loads an embedded texture from one of the plugin assemblies.
    /// </summary>
    /// <param name="name">The logical name of the resource.</param>
    /// <returns>An instance of <see cref="IDalamudTextureWrap"/> that wraps the resource.</returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static IDalamudTextureWrap GetEmbeddedTexture(string name)
    {
        return DalamudServices.TextureProvider.GetFromManifestResource(Assembly.GetExecutingAssembly(), name).GetWrapOrEmpty();
    }

    /// <summary>
    /// Gets the uld icon for the specified part ID.
    /// </summary>
    /// <param name="uldPath">The uld path to get for specifically the .uld file in question</param>
    /// <param name="partsId">What parts group to look for</param>
    /// <param name="partId">What part to use of parts group</param>
    /// <param name="style">The style of the uld</param>
    /// <returns><see cref="UldIcon"/></returns>
    /// <exception cref="InvalidOperationException"></exception>
    internal static unsafe UldIcon? LoadUld(string uldPath, int partsId, int partId, UldStyle style = UldStyle.Default)
    {
        if (!uldPath.EndsWith(".uld")) {
            if (uldPath.Contains('.'))
                throw new ArgumentException("Not a path to uld file.", nameof(uldPath));
            uldPath += ".uld";
        }

        var uldFile = LoadUldFile(uldPath);

        if (uldFile == null)
            return null;

        var part = uldFile.Parts.First(t => t.Id == partsId);
        var subPart = part.Parts[partId];
        var tex = uldFile.AssetData.First(t => t.Id == subPart.TextureId).Path;
        string texPath;
        fixed (char* p = tex)
            texPath = new string(p);
        var normalTexPath = texPath;
        var scale = 2;
        texPath = texPath[..^4] + "_hr1.tex";
        var texFile = LoadTexture(texPath.Replace("uld/", GetUldStyleString(style)));
        // failed to get hr version of texture? Fallback to normal
        if (texFile == null) {
            scale = 1;
            texFile = LoadTexture(normalTexPath);
            // failed to get normal texture? Something is wrong with uld but ¯\_(ツ)_/¯ can't do much about that one so return null
            if (texFile == null)
                return null;
        }

        var uv = new Vector2(subPart.U, subPart.V) * scale;
        var size = new Vector2(subPart.W, subPart.H) * scale;

        return new UldIcon { Size = size, Texture = texFile, Rect = new Rect(uv, uv + size) };
    }

    private static UldFile? LoadUldFile(string path)
    {
        if (DalamudServices.DataManager == null || DalamudServices.TextureProvider == null)
            throw new InvalidOperationException("Una.Drawing.DrawingLib has not been set-up.");

        if (!PathToUldFileCache.TryGetValue(path, out var uldFile)) {
            uldFile = DalamudServices.DataManager.GetFile<UldFile>(path);
            if (uldFile == null) return null;
            PathToUldFileCache[path] = uldFile;
        }

        return uldFile;
    }

    private static string GetUldStyleString(UldStyle style) => style switch {
        UldStyle.Default => "uld/",
        _ => $"uld/img{(int)style:D2}" // UldStyle.Light is 1 and rest is up from there so this works for new naming scheme
    };

    internal static SKImage? LoadFromBytes(byte[] bytes)
    {
        using MemoryStream stream = new(bytes);
        using SKImage image = SKImage.FromEncodedData(stream);
        using SKBitmap bitmap = SKBitmap.FromImage(image);

        // We need to do some fuckery to swap from BGRA to RGBA...
        SKImageInfo info = new(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);

        IntPtr pixelPtr = Marshal.AllocHGlobal(bitmap.ByteCount);

        Marshal.Copy(bitmap.Bytes, 0, pixelPtr, bitmap.ByteCount);
        SKImage? output = SKImage.FromPixels(info, pixelPtr);

        return output;
    }

    internal static SKImage? LoadIcon(uint iconId)
    {
        if (IconToImageCache.TryGetValue(iconId, out SKImage? cachedImage)) return cachedImage;

        TexFile iconFile;
        string iconPath;

        try {
            (iconFile, iconPath) = GetIconFile(iconId);
        } catch {
            // There are 3 reasons why this can fail and neither of them are
            // reasons for the plugin to crash:
            //   - The icon ID is invalid
            //   - The icon references a file that can not be found or read. (broken texture mods)
            //   - A substituted texture file is corrupted (broken texture mods)
            return null;
        }

        // If the icon size is 0 or negative (a good indicator for more broken texture mods),
        // just return null as well.
        if (iconFile.Header.Width <= 0 || iconFile.Header.Height <= 0) return null;

        try {
            SKImage image = LoadImageFromTexOrPath(iconFile, iconPath) ?? throw new InvalidOperationException($"Failed to load icon {iconId}");

            IconToImageCache[iconId] = image;

            return image;
        } catch (Exception) {
            // As of 07/08/2023, FFXIV introduced BC5/BC7 textures for some
            // icons, which are currently unsupported by Lumia. This causes
            // the ".imageData" getter on iconFile to throw an exception.
            return null;
        }
    }

    internal static SKImage? LoadGfdIcon(BitmapFontIcon fontIcon)
    {
        try {
            GfdIcon icon = GfdIconRepository.GetIcon(fontIcon);
            SKImage atlas = icon.Texture;
            SKRectI uv = new((int)icon.Uv0.X, (int)icon.Uv0.Y, (int)icon.Uv1.X, (int)icon.Uv1.Y);

            int width = uv.Right - uv.Left;
            int height = uv.Bottom - uv.Top;

            SKBitmap subsetBitmap = new SKBitmap(width, height);

            atlas.ReadPixels(
                new SKImageInfo(width, height),
                subsetBitmap.GetPixels(),
                subsetBitmap.RowBytes,
                uv.Left,
                uv.Top
            );

            return SKImage.FromBitmap(subsetBitmap);
        } catch (Exception) {
            // The only reason for this to fail is if a bad texture mod is installed
            // or if the icon ID is invalid.
            return null;
        }
    }

    internal static SKImage? LoadTexture(string path)
    {
        if (PathToImageCache.TryGetValue(path, out SKImage? cachedImage)) return cachedImage;

        if (DalamudServices.DataManager == null || DalamudServices.TextureProvider == null)
            throw new InvalidOperationException("Una.Drawing.DrawingLib has not been set-up.");

        if (!PathToTexFileCache.TryGetValue(path, out var texFile)) {
            path = DalamudServices.TextureSubstitutionProvider.GetSubstitutedPath(path);

            try {
                texFile = Path.IsPathRooted(path)
                    ? DalamudServices.DataManager.GameData.GetFileFromDisk<TexFile>(path)
                    : DalamudServices.DataManager.GetFile<TexFile>(path);
            } catch (Exception e) {
                DebugLogger.Log($"Failed to load texture {path}. Falling back to default. Error: {e.Message}");

                try {
                    texFile = DalamudServices.DataManager.GetFile<TexFile>(path);
                } catch {
                    // this should never happen.
                    return null;
                }
            }

            if (null == texFile) return null;

            PathToTexFileCache[path] = texFile;
        }

        cachedImage = LoadImageFromTexOrPath(texFile, path);
        if (cachedImage == null) return null;
        PathToImageCache[path] = cachedImage;
        return cachedImage;
    }

    /// <summary>
    /// Returns a <see cref="TexFile"/> for the given icon ID.
    /// </summary>
    private static (TexFile iconFile, string iconPath) GetIconFile(uint iconId)
    {
        if (IconToTexFileCache.TryGetValue(iconId, out var cachedIconFile)) return cachedIconFile;

        if (DalamudServices.DataManager == null || DalamudServices.TextureProvider == null)
            throw new InvalidOperationException("Una.Drawing.DrawingLib has not been set-up.");

        string iconPath = DalamudServices.TextureProvider.GetIconPath(new() { IconId = iconId, HiRes = true });

        IconToTexFileCache[iconId] = (GetTextureFile(iconPath) ?? throw new InvalidOperationException($"Failed to load icon {iconId}"), iconPath);

        return IconToTexFileCache[iconId];
    }

    /// <summary>
    /// Returns a <see cref="TexFile"/> for the given game file path if it exists.
    /// </summary>
    private static TexFile? GetTextureFile(string path)
    {
        if (PathToTexFileCache.TryGetValue(path, out var texFile)) return texFile;

        path = DalamudServices.TextureSubstitutionProvider.GetSubstitutedPath(path);

        try {
            texFile = Path.IsPathRooted(path)
                ? DalamudServices.DataManager.GameData.GetFileFromDisk<TexFile>(path)
                : DalamudServices.DataManager.GetFile<TexFile>(path);
        } catch (Exception e) {
            DebugLogger.Log($"Failed to load texture {path}. Falling back to default. Error: {e.Message}");

            try {
                texFile = DalamudServices.DataManager.GetFile<TexFile>(path);
            } catch {
                // this should never happen.
                return null;
            }
        }

        if (null == texFile) return null;

        PathToTexFileCache[path] = texFile;

        return texFile;
    }

    /// <summary>
    /// Returns a <see cref="SKImage"/> for the given <see cref="TexFile"/>.
    /// Loads from game path if BC5/BC7 texture.
    /// </summary>
    private static SKImage? LoadImageFromTexOrPath(TexFile texFile, string path)
    {
        IntPtr pixelPtr;
        int height, width;
        switch (texFile.Header.Format) {
            case TexFile.TextureFormat.BC5:
            case TexFile.TextureFormat.BC7:
                if (!PathToByteArrayCache.TryGetValue(path, out var byteInfo)) {
                    var wrap = DalamudServices.TextureProvider.GetFromGame(path).RentAsync().GetAwaiter().GetResult();
                    var (specs, data) = DalamudServices.TextureReadbackProvider.GetRawImageAsync(wrap, new TextureModificationArgs { DxgiFormat = (int)DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM }).GetAwaiter().GetResult();
                    
                    unsafe {
                        fixed (byte* p = data) {
                            if (specs.Pitch != specs.Width * 4) {
                                for (var i = 0; i < specs.Height; i++) {
                                    new Span<byte>(p + (specs.Pitch * i), specs.Pitch).CopyTo(
                                        new(p + (specs.Width * 4 * i), specs.Pitch));
                                }
                            }
                        }
                    }
                    
                    height = specs.Height;
                    width = specs.Width;
                    PathToByteArrayCache[path] = (data, width, height);
                } else {
                    width  = byteInfo.width;
                    height = byteInfo.height;
                }

                if (!PathToBytePtrCache.TryGetValue(path, out pixelPtr)) {
                    pixelPtr = Marshal.AllocHGlobal(PathToByteArrayCache[path].bytes.Length);
                    Marshal.Copy(PathToByteArrayCache[path].bytes, 0, pixelPtr, PathToByteArrayCache[path].bytes.Length);
                    PathToBytePtrCache[path] = pixelPtr;
                }
                break;
            default:
                if (!PathToBytePtrCache.TryGetValue(path, out pixelPtr)) {
                    pixelPtr = Marshal.AllocHGlobal(texFile.ImageData.Length); 
                    Marshal.Copy(texFile.ImageData, 0, pixelPtr, texFile.ImageData.Length);
                    PathToBytePtrCache[path] = pixelPtr;
                }
                height = texFile.Header.Height;
                width = texFile.Header.Width;
                break;
        }

        SKImageInfo info = new(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        using SKPixmap pixmap = new(info, pixelPtr);
        var image = SKImage.FromPixels(pixmap);

        return image;
    }
}

internal struct UldIcon
{
    public SKImage Texture { get; init; }
    public Rect Rect { get; init; }
    public Vector2 Size { get; set; }
}