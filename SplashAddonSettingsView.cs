using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace SplashAddonPlus
{
    /// <summary>
    /// Settings page built in code (no XAML), so the project builds without the WPF markup compiler.
    /// DataContext is the <see cref="SplashAddonSettings"/> instance.
    /// </summary>
    public class SplashAddonSettingsView : UserControl
    {
        private static readonly IValueConverter Inverse = new BoolConverter(false, true);
        private static readonly IValueConverter DimWhenDisabled = new BoolConverter(1.0, 0.5);
        private static readonly IValueConverter IntToDouble = new IntDoubleConverter();

        /// <param name="resolveGameName">Name of a game in the library for a game ID, or null.</param>
        public SplashAddonSettingsView(IList<string> platforms, Func<string, string> resolveGameName)
        {
            var root = new StackPanel { Margin = new Thickness(20) };

            // ---------- Automatic detection ----------
            root.Children.Add(SectionHeader(Loc.SectionAutoDetect, first: true));
            root.Children.Add(Toggle(Loc.AutoDetectToggle, "AutoDetectFullscreen", bold: true));
            root.Children.Add(Hint(Loc.AutoDetectHint));

            var autoPanel = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
            BindEnabled(autoPanel, "AutoDetectFullscreen", inverse: false);
            autoPanel.Children.Add(Toggle(Loc.WindowedToggle, "DetectWindowedGames", bold: true));
            autoPanel.Children.Add(Hint(Loc.WindowedHint));
            autoPanel.Children.Add(NumberField(Loc.MinimumDisplay, "MinimumDisplaySeconds", Loc.MinimumDisplayHint));
            autoPanel.Children.Add(NumberField(Loc.DetectionDelay, "DetectionDelaySeconds", Loc.DetectionDelayHint));
            autoPanel.Children.Add(Label(Loc.IgnoredProcesses));
            autoPanel.Children.Add(Hint(Loc.IgnoredProcessesHint));
            autoPanel.Children.Add(MultilineBox("IgnoredProcessNamesText", 70));
            root.Children.Add(autoPanel);

            // ---------- Closing ----------
            root.Children.Add(SectionHeader(Loc.SectionClosing));
            root.Children.Add(NumberField(Loc.MaximumWait, "MaximumWaitSeconds", Loc.MaximumWaitHint));
            root.Children.Add(Hint(Loc.ManualCloseHint, emphasize: true));

            // ---------- Appearance ----------
            root.Children.Add(SectionHeader(Loc.SectionAppearance));
            root.Children.Add(Choice(Loc.LogoPositionLabel, "LogoPosition",
                Option(LogoPlacement.BottomLeft, Loc.LogoBottomLeft),
                Option(LogoPlacement.BottomCenter, Loc.LogoBottomCenter),
                Option(LogoPlacement.BottomRight, Loc.LogoBottomRight),
                Option(LogoPlacement.Center, Loc.LogoCenter),
                Option(LogoPlacement.TopLeft, Loc.LogoTopLeft),
                Option(LogoPlacement.TopRight, Loc.LogoTopRight)));
            root.Children.Add(PercentSlider(Loc.LogoWidth, "LogoWidthPercent", Loc.LogoWidthHint));
            root.Children.Add(PercentSlider(Loc.BackgroundDim, "BackgroundDimPercent", null));
            root.Children.Add(Toggle(Loc.VignetteToggle, "BackgroundVignette"));
            root.Children.Add(Toggle(Loc.ZoomToggle, "BackgroundZoom"));

            // ---------- Per-game exceptions ----------
            root.Children.Add(SectionHeader(Loc.SectionExceptions));
            root.Children.Add(TipBox(Loc.ExceptionsTip));

            root.Children.Add(Label(Loc.GameDurations));
            root.Children.Add(new GameListEditor(withTime: true, resolveGameName));

            root.Children.Add(Label(Loc.ExcludedGames));
            root.Children.Add(new GameListEditor(withTime: false, resolveGameName));

            // ---------- Fixed timers (advanced, collapsed while detection is on) ----------
            var fixedPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            fixedPanel.Children.Add(Hint(Loc.FixedTimeHint));
            fixedPanel.Children.Add(Toggle(Loc.WaitForStartedToggle, "UseGameStartedTimer"));

            var timersPanel = new StackPanel();
            BindEnabled(timersPanel, "AutoDetectFullscreen", inverse: true);
            timersPanel.Children.Add(NumberField(Loc.DefaultDuration, "SplashScreenDuration", null));
            timersPanel.Children.Add(Toggle(Loc.PlatformTimersToggle, "UsePlatformSpecificTimers", bold: true));
            var platformEditor = new PlatformListEditor(platforms);
            BindEnabled(platformEditor, "UsePlatformSpecificTimers", inverse: false);
            timersPanel.Children.Add(platformEditor);
            timersPanel.Children.Add(Hint(Loc.PriorityHint));
            fixedPanel.Children.Add(timersPanel);

            var advanced = new Expander
            {
                Header = new TextBlock { Text = Loc.SectionFixedTime, FontSize = 18, FontWeight = FontWeights.SemiBold },
                Margin = new Thickness(0, 25, 0, 0),
                Content = fixedPanel
            };
            root.Children.Add(advanced);

            // ---------- Diagnostics ----------
            root.Children.Add(SectionHeader(Loc.SectionDiagnostics));
            root.Children.Add(Toggle(Loc.DiagnosticsToggle, "EnableDiagnosticLogging", bold: true));
            root.Children.Add(Hint(Loc.DiagnosticsHint));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };

            // Open the advanced section right away only for people who use the fixed timers.
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is SplashAddonSettings settings)
                {
                    advanced.IsExpanded = !settings.AutoDetectFullscreen;
                }
            };
        }

        private static KeyValuePair<object, string> Option(object value, string label) => new KeyValuePair<object, string>(value, label);

        private static UIElement Choice(string label, string path, params KeyValuePair<object, string>[] options)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var combo = new ComboBox
            {
                ItemsSource = options,
                DisplayMemberPath = "Value",
                SelectedValuePath = "Key",
                Width = 280,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            combo.SetBinding(Selector.SelectedValueProperty,
                new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(combo);
            return panel;
        }

        /// <summary>A 0–90% slider with the current value shown next to it.</summary>
        private static UIElement PercentSlider(string label, string path, string hint)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, hint == null ? 15 : 5) };
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = SplashAddonSettings.MaxPercent,
                SmallChange = 1,
                LargeChange = 5,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Width = 280,
                VerticalAlignment = VerticalAlignment.Center
            };
            slider.SetBinding(RangeBase.ValueProperty,
                new Binding(path) { Mode = BindingMode.TwoWay, Converter = IntToDouble, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            row.Children.Add(slider);

            var value = new TextBlock { Width = 50, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            value.SetBinding(TextBlock.TextProperty, new Binding(path) { StringFormat = "{0}%" });
            row.Children.Add(value);
            panel.Children.Add(row);

            if (hint != null)
            {
                TextBlock hintBlock = Hint(hint);
                hintBlock.Margin = new Thickness(0, 0, 0, 15);
                panel.Children.Add(hintBlock);
            }

            return panel;
        }

        /// <summary>
        /// Games from the per-game settings shown by name, each with a Remove button (and the seconds for fixed-time games).
        /// Games are added from the game context menu.
        /// </summary>
        private sealed class GameListEditor : StackPanel
        {
            private const double NameWidth = 350;

            private readonly bool _withTime;
            private readonly Func<string, string> _resolveName;
            private readonly StackPanel _rows = new StackPanel();
            private SplashAddonSettings _settings;

            public GameListEditor(bool withTime, Func<string, string> resolveName)
            {
                _withTime = withTime;
                _resolveName = resolveName;
                Margin = new Thickness(0, 0, 0, 10);

                if (withTime)
                {
                    var header = new Grid();
                    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
                    header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                    AddToGrid(header, new TextBlock { Text = Loc.GameColumn, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) }, 0, 0);
                    AddToGrid(header, new TextBlock { Text = Loc.TimeColumn, FontWeight = FontWeights.Bold, Margin = new Thickness(5, 0, 0, 5) }, 0, 1);
                    Children.Add(header);
                }

                Children.Add(_rows);

                DataContextChanged += (s, e) => Attach(e.NewValue as SplashAddonSettings);
                Unloaded += (s, e) => Attach(null);
                Loaded += (s, e) =>
                {
                    if (_settings == null)
                    {
                        Attach(DataContext as SplashAddonSettings);
                    }
                };
            }

            private string WatchedProperty => _withTime ? "GameSpecificDurations" : "ExcludedGameIds";

            private void Attach(SplashAddonSettings settings)
            {
                if (_settings != null)
                {
                    _settings.PropertyChanged -= OnSettingsChanged;
                }

                _settings = settings;
                if (_settings != null)
                {
                    _settings.PropertyChanged += OnSettingsChanged;
                }

                Rebuild();
            }

            // Removing a game, or Cancel in the settings dialog, replaces the whole list.
            private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == WatchedProperty)
                {
                    Rebuild();
                }
            }

            private void Rebuild()
            {
                _rows.Children.Clear();
                if (_settings == null)
                {
                    return;
                }

                IEnumerable<string> ids = _withTime
                    ? (IEnumerable<string>)(_settings.GameSpecificDurations?.Keys ?? new Dictionary<string, int>().Keys)
                    : _settings.ExcludedGameIds;
                List<GameEntry> games = GameEntries.Sort(ids, _resolveName);

                if (games.Count == 0)
                {
                    TextBlock empty = Hint(Loc.NoGames);
                    empty.Margin = new Thickness(0, 0, 0, 5);
                    _rows.Children.Add(empty);
                    return;
                }

                foreach (GameEntry game in games)
                {
                    _rows.Children.Add(CreateRow(game));
                }
            }

            private UIElement CreateRow(GameEntry game)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
                if (_withTime)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                }

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                AddToGrid(row, new TextBlock
                {
                    Text = game.Name ?? Loc.UnknownGame(game.Id),
                    ToolTip = game.Id,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = game.Name == null ? 0.6 : 1.0
                }, 0, 0);

                int column = 1;
                if (_withTime)
                {
                    int current;
                    _settings.GameSpecificDurations.TryGetValue(game.Id, out current);
                    var seconds = new TextBox
                    {
                        Text = current.ToString(CultureInfo.InvariantCulture),
                        Width = 80,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(5, 0, 0, 0)
                    };

                    // Written straight into the dictionary: replacing it would rebuild the rows and lose the caret.
                    // Invalid or zero values are reported by the settings check on Save.
                    seconds.TextChanged += (s, e) =>
                    {
                        if (_settings?.GameSpecificDurations != null && int.TryParse(seconds.Text.Trim(), out int value))
                        {
                            _settings.GameSpecificDurations[game.Id] = value;
                        }
                    };
                    AddToGrid(row, seconds, 0, column++);
                }

                var remove = new Button { Content = Loc.Remove, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(5, 0, 0, 0) };
                remove.Click += (s, e) =>
                {
                    if (_settings == null)
                    {
                        return;
                    }

                    if (_withTime)
                    {
                        _settings.SetGameSpecificDuration(new[] { game.Id }, null);
                    }
                    else
                    {
                        _settings.SetExcluded(new[] { game.Id }, false);
                    }
                };
                AddToGrid(row, remove, 0, column);
                return row;
            }
        }

        /// <summary>Rows of platform + seconds, with buttons to add and remove rows.</summary>
        private sealed class PlatformListEditor : StackPanel
        {
            private readonly IList<string> _platforms;
            private readonly StackPanel _rows = new StackPanel();
            private SplashAddonSettings _settings;
            private ObservableCollection<PlatformDuration> _collection;

            public PlatformListEditor(IList<string> platforms)
            {
                _platforms = platforms ?? new List<string>();
                Margin = new Thickness(0, 0, 0, 10);

                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                AddToGrid(header, new TextBlock { Text = Loc.PlatformColumn, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 5) }, 0, 0);
                AddToGrid(header, new TextBlock { Text = Loc.TimeColumn, FontWeight = FontWeights.Bold, Margin = new Thickness(5, 0, 0, 5) }, 0, 1);
                Children.Add(header);
                Children.Add(_rows);

                var add = new Button
                {
                    Content = Loc.AddPlatform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 4, 12, 4),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                add.Click += (s, e) => _collection?.Add(new PlatformDuration());
                Children.Add(add);

                DataContextChanged += (s, e) => Attach(e.NewValue as SplashAddonSettings);

                // Let go of the settings when the dialog closes, so closed pages are not kept alive by event handlers.
                Unloaded += (s, e) => Attach(null);
                Loaded += (s, e) =>
                {
                    if (_settings == null)
                    {
                        Attach(DataContext as SplashAddonSettings);
                    }
                };
            }

            private void Attach(SplashAddonSettings settings)
            {
                if (_settings != null)
                {
                    _settings.PropertyChanged -= OnSettingsChanged;
                }

                _settings = settings;
                if (_settings != null)
                {
                    _settings.PropertyChanged += OnSettingsChanged;
                }

                AttachCollection(_settings?.PlatformDurations);
            }

            private void OnSettingsChanged(object sender, PropertyChangedEventArgs e)
            {
                // Cancel in the settings dialog replaces the whole collection.
                if (e.PropertyName == "PlatformDurations")
                {
                    AttachCollection(_settings?.PlatformDurations);
                }
            }

            private void AttachCollection(ObservableCollection<PlatformDuration> collection)
            {
                if (_collection != null)
                {
                    _collection.CollectionChanged -= OnCollectionChanged;
                }

                _collection = collection;
                if (_collection != null)
                {
                    _collection.CollectionChanged += OnCollectionChanged;
                }

                Rebuild();
            }

            private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => Rebuild();

            private void Rebuild()
            {
                _rows.Children.Clear();
                if (_collection == null)
                {
                    return;
                }

                foreach (PlatformDuration entry in _collection)
                {
                    _rows.Children.Add(CreateRow(entry));
                }
            }

            private UIElement CreateRow(PlatformDuration entry)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var choices = new List<string>(_platforms);
                if (!string.IsNullOrEmpty(entry.Platform) && !choices.Contains(entry.Platform))
                {
                    choices.Add(entry.Platform);
                }

                var combo = new ComboBox { ItemsSource = choices, Width = 280, HorizontalAlignment = HorizontalAlignment.Left };
                combo.SetBinding(Selector.SelectedItemProperty,
                    new Binding("Platform") { Source = entry, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                AddToGrid(row, combo, 0, 0);

                var seconds = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(5, 0, 0, 0) };
                seconds.SetBinding(TextBox.TextProperty,
                    new Binding("Seconds") { Source = entry, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                AddToGrid(row, seconds, 0, 1);

                var remove = new Button { Content = Loc.Remove, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(5, 0, 0, 0) };
                remove.Click += (s, e) => _collection?.Remove(entry);
                AddToGrid(row, remove, 0, 2);
                return row;
            }
        }

        private static void AddToGrid(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private static void BindEnabled(FrameworkElement element, string path, bool inverse)
        {
            element.SetBinding(IsEnabledProperty, new Binding(path) { Converter = inverse ? Inverse : null });
            element.SetBinding(OpacityProperty, new Binding("IsEnabled")
            {
                RelativeSource = RelativeSource.Self,
                Converter = DimWhenDisabled
            });
        }

        private static TextBlock SectionHeader(string text, bool first = false)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, first ? 0 : 25, 0, 10)
            };
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 5)
            };
        }

        private static TextBlock Hint(string text, bool emphasize = false)
        {
            return new TextBlock
            {
                Text = text,
                FontStyle = FontStyles.Italic,
                FontSize = 11,
                Foreground = emphasize ? Brushes.Orange : Brushes.Gray,
                FontWeight = emphasize ? FontWeights.Bold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private static UIElement TipBox(string text)
        {
            return new Border
            {
                BorderBrush = Brushes.Orange,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            };
        }

        private static CheckBox Toggle(string text, string path, bool bold = false)
        {
            var checkBox = new CheckBox
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return checkBox;
        }

        private static UIElement NumberField(string label, string path, string hint)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            });

            var box = new TextBox
            {
                Width = 100,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, hint == null ? 15 : 5)
            };
            box.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(box);

            if (hint != null)
            {
                TextBlock hintBlock = Hint(hint);
                hintBlock.Margin = new Thickness(0, 0, 0, 15);
                panel.Children.Add(hintBlock);
            }

            return panel;
        }

        private static TextBox MultilineBox(string path, double height)
        {
            var box = new TextBox
            {
                Height = height,
                AcceptsReturn = true,
                AcceptsTab = false,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(5),
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 0, 0, 10)
            };
            box.SetBinding(TextBox.TextProperty, new Binding(path) { UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });

            // The settings dialog may treat Enter as "Save"; keep Enter inside the text box instead.
            box.PreviewKeyDown += (sender, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Return)
                {
                    return;
                }

                int caret = box.SelectionStart;
                box.SelectedText = Environment.NewLine;
                box.SelectionLength = 0;
                box.CaretIndex = caret + Environment.NewLine.Length;
                e.Handled = true;
            };

            return box;
        }

        /// <summary>Slider values are doubles, the settings store whole percents.</summary>
        private sealed class IntDoubleConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is int number ? (double)number : 0.0;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is double number ? (int)Math.Round(number) : 0;
            }
        }

        private sealed class BoolConverter : IValueConverter
        {
            private readonly object _whenTrue;
            private readonly object _whenFalse;

            public BoolConverter(object whenTrue, object whenFalse)
            {
                _whenTrue = whenTrue;
                _whenFalse = whenFalse;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return value is bool b && b ? _whenTrue : _whenFalse;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return Equals(value, _whenTrue);
            }
        }
    }
}
