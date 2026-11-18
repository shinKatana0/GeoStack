// Copyright 2026 shinKatana0
// SPDX-License-Identifier: Apache-2.0
using System.IO;
using System.Windows.Media.Imaging;

namespace GeoStack.App;

public static class RasterTileDecoder
{
    public static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand).Frames[0];
        if (header.PixelWidth is < 1 or > 2048 || header.PixelHeight != header.PixelWidth) throw new InvalidDataException("MapEncodingUnsupported");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream; image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 256; image.EndInit(); image.Freeze(); return image;
    }
}
