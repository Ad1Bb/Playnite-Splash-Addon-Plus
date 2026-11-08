using System;
using System.IO;
using System.Net;
using System.Windows.Media.Imaging;

namespace SplashAddonPlus
{
    /// <summary>
    /// Loads images off the UI thread, decoded no larger than needed for the screen, and frozen
    /// so the UI thread can use them. Every method returns null instead of throwing.
    /// </summary>
    internal static class ImageLoader
    {
        private const int DownloadTimeoutMs = 8000;
        private const int MaxDownloadBytes = 40 * 1024 * 1024;

        /// <summary>
        /// Loads a file path or http(s) URL. With <paramref name="cover"/> the image is decoded just large enough
        /// to fill targetWidth x targetHeight (cropping allowed); otherwise just large enough to fit inside it.
        /// A target of 0 means "no limit" for that side.
        /// </summary>
        public static BitmapSource Load(string source, int targetWidth, int targetHeight, bool cover)
        {
            byte[] data = ReadBytes(source);
            if (data == null || data.Length == 0)
            {
                return null;
            }

            try
            {
                int pixelWidth;
                int pixelHeight;
                using (var probe = new MemoryStream(data, false))
                {
                    BitmapFrame frame = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
                    pixelWidth = frame.PixelWidth;
                    pixelHeight = frame.PixelHeight;
                }

                int decodeWidth = ComputeDecodeWidth(pixelWidth, pixelHeight, targetWidth, targetHeight, cover);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new MemoryStream(data, false);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }

                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Width to decode an image of imageWidth x imageHeight at, or 0 to keep its own size
        /// (it is already small enough, or the size is unknown).
        /// </summary>
        internal static int ComputeDecodeWidth(int imageWidth, int imageHeight, int targetWidth, int targetHeight, bool cover)
        {
            if (imageWidth <= 0 || imageHeight <= 0 || (targetWidth <= 0 && targetHeight <= 0))
            {
                return 0;
            }

            double scaleX = targetWidth > 0 ? (double)targetWidth / imageWidth : double.NaN;
            double scaleY = targetHeight > 0 ? (double)targetHeight / imageHeight : double.NaN;

            double scale;
            if (double.IsNaN(scaleX))
            {
                scale = scaleY;
            }
            else if (double.IsNaN(scaleY))
            {
                scale = scaleX;
            }
            else
            {
                scale = cover ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            }

            if (scale >= 1.0)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Ceiling(imageWidth * scale));
        }

        private static byte[] ReadBytes(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            try
            {
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return Download(source);
                }

                return File.Exists(source) ? File.ReadAllBytes(source) : null;
            }
            catch
            {
                return null;
            }
        }

        private static byte[] Download(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = DownloadTimeoutMs;
            request.ReadWriteTimeout = DownloadTimeoutMs;
            request.UserAgent = "Playnite Splash Addon Plus";

            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (var buffer = new MemoryStream())
            {
                if (body == null)
                {
                    return null;
                }

                var chunk = new byte[81920];
                int read;
                while ((read = body.Read(chunk, 0, chunk.Length)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaxDownloadBytes)
                    {
                        return null;
                    }
                }

                return buffer.ToArray();
            }
        }
    }
}
