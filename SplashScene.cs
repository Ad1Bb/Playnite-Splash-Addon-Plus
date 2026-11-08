using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Playnite.SDK;
using Playnite.SDK.Models;

namespace SplashAddonPlus
{
    /// <summary>How the splash screen looks.</summary>
    internal sealed class SplashAppearance
    {
        public LogoPlacement LogoPosition { get; set; } = LogoPlacement.BottomLeft;
        public int LogoWidthPercent { get; set; } = 16;
        public int BackgroundDimPercent { get; set; }
        public bool Vignette { get; set; }
        public bool Zoom { get; set; }
    }

    /// <summary>Where the splash images of one game come from (file paths or URLs, any of them may be null).</summary>
    internal sealed class SplashImageSources
    {
        public string Background { get; set; }
        public string Cover { get; set; }
        public string Logo { get; set; }

        public static SplashImageSources ForGame(Game game)
        {
            return new SplashImageSources
            {
                Background = ResolveDatabaseImage(game.BackgroundImage),
                Cover = ResolveDatabaseImage(game.CoverImage),
                Logo = ResolveLogo(game)
            };
        }

        private static string ResolveDatabaseImage(string image)
        {
            if (string.IsNullOrEmpty(image))
            {
                return null;
            }

            try
            {
                if (image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return image;
                }

                string path = API.Instance.Database.GetFullFilePath(image);
                return !string.IsNullOrEmpty(path) && File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }

        // Logos are stored by the Extra Metadata Loader add-on.
        private static string ResolveLogo(Game game)
        {
            try
            {
                string path = System.IO.Path.Combine(API.Instance.Paths.ConfigurationPath, "ExtraMetadata", "Games", game.Id.ToString(), "Logo.png");
                return File.Exists(path) ? path : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Content of the splash window: background, optional dimming and vignette, logo.
    /// Images are loaded in the background and fade in when ready, so showing the splash never waits for them.
    /// When a game has no background, its cover is used instead (blurred, with the cover itself in the middle if there is no logo).
    /// </summary>
    internal sealed class SplashScene
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        private static readonly Duration ImageFade = new Duration(TimeSpan.FromSeconds(0.4));
        private const double BlurredCoverScale = 1.12; // hides the soft edges of the blur
        private const double ZoomAmount = 1.08;
        private static readonly TimeSpan ZoomDuration = TimeSpan.FromSeconds(30);
        private const double LogoMaxHeightShare = 0.4;
        private const double CoverArtHeightShare = 0.6;
        private const double MarginShare = 0.025;

        private readonly SplashAppearance _appearance;
        private readonly Grid _root = new Grid { Background = Brushes.Black, ClipToBounds = true };
        private readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
        private readonly Image _background;
        private readonly Rectangle _dim;
        private readonly Image _coverArt;
        private readonly Image _logo;

        public SplashScene(SplashAppearance appearance)
        {
            _appearance = appearance ?? new SplashAppearance();

            _background = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _zoom,
                Opacity = 0
            };
            _root.Children.Add(_background);

            _dim = new Rectangle
            {
                Fill = Brushes.Black,
                Opacity = Clamp(_appearance.BackgroundDimPercent, 0, SplashAddonSettings.MaxPercent) / 100.0,
                IsHitTestVisible = false
            };
            _root.Children.Add(_dim);

            if (_appearance.Vignette)
            {
                var vignette = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.75,
                    RadiusY = 0.75
                };
                vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.45));
                vignette.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0, 0, 0), 1.0));
                vignette.Freeze();
                _root.Children.Add(new Rectangle { Fill = vignette, IsHitTestVisible = false });
            }

            _coverArt = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0
            };
            _root.Children.Add(_coverArt);

            _logo = new Image { Stretch = Stretch.Uniform, Opacity = 0 };
            PlaceLogo(_logo, _appearance.LogoPosition);
            _root.Children.Add(_logo);

            _root.SizeChanged += (s, e) => UpdateSizes();
        }

        public UIElement Root => _root;

        /// <summary>Logo width in percent of the screen; 0 means no logo.</summary>
        private int LogoPercent => Clamp(_appearance.LogoWidthPercent, 0, SplashAddonSettings.MaxPercent);

        /// <summary>Starts loading the images; <paramref name="screen"/> is the monitor size in pixels.</summary>
        public void LoadImagesAsync(SplashImageSources sources, MonitorArea screen)
        {
            if (sources == null)
            {
                return;
            }

            int width = screen?.Width > 0 ? screen.Width : 1920;
            int height = screen?.Height > 0 ? screen.Height : 1080;
            int logoPercent = LogoPercent;
            int logoWidth = Math.Max(1, width * logoPercent / 100);
            int logoHeight = (int)(height * LogoMaxHeightShare);

            Task.Run(() =>
            {
                var loaded = new LoadedImages
                {
                    Background = ImageLoader.Load(sources.Background, width, height, cover: true),
                    Logo = logoPercent > 0 ? ImageLoader.Load(sources.Logo, logoWidth, logoHeight, cover: false) : null
                };

                if (loaded.Background == null)
                {
                    // A small decode is enough for a blurred backdrop, and much cheaper.
                    loaded.BlurredCover = ImageLoader.Load(sources.Cover, 360, 0, cover: false);
                    if (loaded.Logo == null)
                    {
                        loaded.CoverArt = ImageLoader.Load(sources.Cover, 0, (int)(height * CoverArtHeightShare), cover: false);
                    }
                }

                return loaded;
            }).ContinueWith(task =>
            {
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    return;
                }

                _root.Dispatcher.BeginInvoke((Action)(() => Apply(task.Result)));
            }, TaskScheduler.Default);
        }

        private void Apply(LoadedImages images)
        {
            try
            {
                double baseScale = 1.0;
                if (images.Background != null)
                {
                    _background.Source = images.Background;
                }
                else if (images.BlurredCover != null)
                {
                    _background.Source = images.BlurredCover;
                    _background.Effect = new BlurEffect { Radius = 40, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
                    baseScale = BlurredCoverScale;
                    _dim.Opacity = Math.Max(_dim.Opacity, 0.35);
                }

                _zoom.ScaleX = baseScale;
                _zoom.ScaleY = baseScale;

                if (_background.Source != null)
                {
                    FadeIn(_background);
                    if (_appearance.Zoom)
                    {
                        var zoom = new DoubleAnimation(baseScale, baseScale * ZoomAmount, ZoomDuration)
                        {
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                        };
                        _zoom.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
                        _zoom.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
                    }
                }

                if (images.CoverArt != null)
                {
                    _coverArt.Source = images.CoverArt;
                    FadeIn(_coverArt);
                }

                if (images.Logo != null)
                {
                    _logo.Source = images.Logo;
                    FadeIn(_logo);
                }

                UpdateSizes();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not show splash screen images.");
            }
        }

        private void UpdateSizes()
        {
            double width = _root.ActualWidth;
            double height = _root.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double margin = Math.Min(width, height) * MarginShare;
            _logo.Width = width * LogoPercent / 100.0;
            _logo.MaxHeight = height * LogoMaxHeightShare;
            _logo.Margin = new Thickness(margin);
            _coverArt.Height = height * CoverArtHeightShare;
        }

        private static void PlaceLogo(FrameworkElement logo, LogoPlacement placement)
        {
            switch (placement)
            {
                case LogoPlacement.BottomCenter:
                    logo.HorizontalAlignment = HorizontalAlignment.Center;
                    logo.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case LogoPlacement.BottomRight:
                    logo.HorizontalAlignment = HorizontalAlignment.Right;
                    logo.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
                case LogoPlacement.Center:
                    logo.HorizontalAlignment = HorizontalAlignment.Center;
                    logo.VerticalAlignment = VerticalAlignment.Center;
                    break;
                case LogoPlacement.TopLeft:
                    logo.HorizontalAlignment = HorizontalAlignment.Left;
                    logo.VerticalAlignment = VerticalAlignment.Top;
                    break;
                case LogoPlacement.TopRight:
                    logo.HorizontalAlignment = HorizontalAlignment.Right;
                    logo.VerticalAlignment = VerticalAlignment.Top;
                    break;
                default:
                    logo.HorizontalAlignment = HorizontalAlignment.Left;
                    logo.VerticalAlignment = VerticalAlignment.Bottom;
                    break;
            }
        }

        private static void FadeIn(UIElement element)
        {
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.0, 1.0, ImageFade));
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private sealed class LoadedImages
        {
            public BitmapSource Background { get; set; }
            public BitmapSource BlurredCover { get; set; }
            public BitmapSource CoverArt { get; set; }
            public BitmapSource Logo { get; set; }
        }
    }
}
