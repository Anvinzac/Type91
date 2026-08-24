using RectifyPad.Classes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Shapes;

namespace RectifyPad
{
    public sealed class WordDecor
    {
        public string Text { get; set; } = "";
        public string Weight { get; set; } = "SemiBold";
        public string Color { get; set; } = "Black";
        public string Highlight { get; set; } = "None";
        public bool Selected { get; set; }
        public bool HasSaved { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
    }

    public sealed class ColorChoice
    {
        public string Name { get; set; }
        public SolidColorBrush Preview { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal sealed class CanvasSettings
    {
        public string FontFamily { get; set; } = "Be Vietnam Pro";
        public string FontSize { get; set; } = "96";
        public string FontWeight { get; set; } = "SemiBold";
        public string Alignment { get; set; } = "Center";
        public string LineSpacing { get; set; } = "1.1";
        public string TextColor { get; set; } = "Black";
        public string BackgroundColor { get; set; } = "#FAF4EC";
        public bool UseRoundedRect { get; set; }
    }

    public sealed partial class MainPage : Page
    {
        public ObservableCollection<LibraryShelf> Libraries { get; set; } = new ObservableCollection<LibraryShelf>();
        public ObservableCollection<Topic> Topics { get; set; } = new ObservableCollection<Topic>();
        private LibraryShelf _currentLibrary;
        private readonly HashSet<int> _expandedShelves = new HashSet<int>();
        private string _libraryQuery = "";
        private Topic _currentTopic;
        private int _currentPageIndex = 0;
        private bool _isUpdatingEditor = false;
        private bool _isLoadingSettings = true;
        private bool _isUpdatingPager;
        private bool _isPagingOverflow;
        private bool _isFitting;
        private bool _isCarouselSnapping;
        private readonly HashSet<Editor> _fitQueued = new HashSet<Editor>();
        private int _scrollGeneration;
        private double _slotWidth = 400;
        private double _sidePad = 24;
        private double _cardWidth = 300;
        private readonly List<SlideVisual> _slides = new List<SlideVisual>();
        private const int NormalWordCount = 24;
        private const int ExtraWordCount = 8;
        private const int MaxWordsPerScreen = NormalWordCount + ExtraWordCount;
        private const float MinScaleFactor = 0.65f;
        private static readonly string[][] PaperSwatches =
        {
            new[] { "Kem", "#FFF6E8" },
            new[] { "Giấy", "#FAF4EC" },
            new[] { "Bạc", "#E8E6E3" },
            new[] { "Hồng", "#F8D7DA" },
            new[] { "Đào", "#FAD9C8" },
            new[] { "Trà", "#E7F0E4" },
            new[] { "Ngọc", "#D7EFE8" },
            new[] { "Tím", "#E8DFF5" },
            new[] { "Vàng", "#F4E3B2" },
            new[] { "Đêm", "#1B1B2F" },
            new[] { "Biển", "#0B3D4A" },
            new[] { "Rêu", "#2F3B2E" },
            new[] { "Cà phê", "#3B2416" },
            new[] { "Mực", "#121212" },
            new[] { "Hoàng hôn", "#C45C26" },
            new[] { "Xám", "#2C2C2C" }
        };
        private Editor _currentEditor;
        private Border _currentRoot;
        private Border _currentTextContainer;
        private DatabaseService _dbService;
        private CanvasSettings _settings = new CanvasSettings();

        public MainPage()
        {
            this.InitializeComponent();
            PopulateColorCombos();
            _dbService = new DatabaseService();
            CarouselScroller.PointerWheelChanged += CarouselScroller_PointerWheelChanged;

            this.Loaded += async (s, e) =>
            {
                string trace = System.IO.Path.Combine(ApplicationData.Current.LocalFolder.Path, "startup-trace.log");
                try
                {
                    File.WriteAllText(trace, "loaded\n");
                    try
                    {
                        Window.Current.SetTitleBar(AppTitleBar);
                    }
                    catch (ArgumentException)
                    {
                    }

                    File.AppendAllText(trace, "titlebar\n");
                    Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated += OnAcceleratorKey;
                    File.AppendAllText(trace, "keys\n");
                    await _dbService.InitializeDatabaseAsync();
                    File.AppendAllText(trace, "db\n");
                    await LoadSettingsAsync();
                    File.AppendAllText(trace, "settings\n");
                    await LoadDatabaseAsync();
                    File.AppendAllText(trace, "ui\n");
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(trace, "fail " + ex + "\n");
                        await ShowMessageAsync("Startup failed", ex.GetType().Name + ": " + ex.Message);
                    }
                    catch (Exception inner)
                    {
                        File.AppendAllText(trace, "dialog " + inner + "\n");
                    }
                }
            };

            this.Unloaded += (s, e) =>
            {
                Window.Current.CoreWindow.Dispatcher.AcceleratorKeyActivated -= OnAcceleratorKey;
            };
        }

        private void OnAcceleratorKey(CoreDispatcher sender, AcceleratorKeyEventArgs e)
        {
            if (e.EventType != CoreAcceleratorKeyEventType.KeyDown &&
                e.EventType != CoreAcceleratorKeyEventType.SystemKeyDown)
            {
                return;
            }

            bool isCtrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (!isCtrl || _currentTopic == null)
            {
                return;
            }

            if (e.VirtualKey == VirtualKey.Left || e.VirtualKey == VirtualKey.Up)
            {
                e.Handled = true;
                var ignore = GoToPageAsync(_currentPageIndex - 1, false);
            }
            else if (e.VirtualKey == VirtualKey.Right || e.VirtualKey == VirtualKey.Down)
            {
                e.Handled = true;
                var ignore = GoToPageAsync(_currentPageIndex + 1, true);
            }
        }

        private async void PrevPageButton_Click(object sender, RoutedEventArgs e)
        {
            await GoToPageAsync(_currentPageIndex - 1, false);
        }

        private async void NextPageButton_Click(object sender, RoutedEventArgs e)
        {
            await GoToPageAsync(_currentPageIndex + 1, true);
        }

        private async Task LoadSettingsAsync()
        {
            _isLoadingSettings = true;
            try
            {
                _settings = await LoadCanvasSettingsAsync();
                SelectComboItem(FontFamilyCombo, _settings.FontFamily);
                SelectComboItem(FontSizeCombo, _settings.FontSize);
                SelectComboItem(LineSpacingCombo, _settings.LineSpacing);
                SelectComboItem(FontWeightCombo, _settings.FontWeight);
                SelectComboItem(AlignmentCombo, _settings.Alignment);
                SelectComboItem(TextColorCombo, _settings.TextColor);
                SelectBackgroundByHex(_settings.BackgroundColor);
                RoundedRectToggle.IsOn = _settings.UseRoundedRect;
                ApplyRoundedRect();
                ApplyCanvasBackground();
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private async Task PersistSettingsAsync()
        {
            if (_isLoadingSettings)
            {
                return;
            }

            ReadSettingsFromUi();
            await SaveCanvasSettingsAsync(_settings);
            await SaveCurrentPageStyleAsync();
        }

        private void ReadSettingsFromUi()
        {
            if (FontFamilyCombo?.SelectedItem != null)
            {
                _settings.FontFamily = FontFamilyCombo.SelectedItem.ToString();
            }

            if (FontSizeCombo?.SelectedItem != null)
            {
                _settings.FontSize = FontSizeCombo.SelectedItem.ToString();
            }

            if (LineSpacingCombo?.SelectedItem != null)
            {
                _settings.LineSpacing = LineSpacingCombo.SelectedItem.ToString();
            }

            if (FontWeightCombo?.SelectedItem != null)
            {
                _settings.FontWeight = FontWeightCombo.SelectedItem.ToString();
            }

            if (AlignmentCombo?.SelectedItem != null)
            {
                _settings.Alignment = AlignmentCombo.SelectedItem.ToString();
            }

            if (TextColorCombo?.SelectedItem != null)
            {
                _settings.TextColor = TextColorCombo.SelectedItem.ToString();
            }

            if (BackgroundCombo?.SelectedItem != null)
            {
                _settings.BackgroundColor = BackgroundHexFromName(BackgroundCombo.SelectedItem.ToString());
            }

            if (RoundedRectToggle != null)
            {
                _settings.UseRoundedRect = RoundedRectToggle.IsOn;
            }
        }

        private void PopulateColorCombos()
        {
            if (TextColorCombo != null)
            {
                TextColorCombo.Items.Clear();
                TextColorCombo.Items.Add(MakeColorChoice("Black", Colors.Black));
                TextColorCombo.Items.Add(MakeColorChoice("Charcoal", ParseHex("#2B2B2B")));
                TextColorCombo.Items.Add(MakeColorChoice("Navy", ParseHex("#1B365D")));
                TextColorCombo.Items.Add(MakeColorChoice("Dark Gray", Colors.DarkGray));
                TextColorCombo.Items.Add(MakeColorChoice("Brown", ParseHex("#5C4033")));
                TextColorCombo.Items.Add(MakeColorChoice("Maroon", ParseHex("#7A1F2B")));
                TextColorCombo.Items.Add(MakeColorChoice("Olive", ParseHex("#4A5D23")));
                TextColorCombo.Items.Add(MakeColorChoice("Forest", ParseHex("#1F4D3A")));
                TextColorCombo.Items.Add(MakeColorChoice("Teal", ParseHex("#0F6B6B")));
                TextColorCombo.Items.Add(MakeColorChoice("White", Colors.White));
                TextColorCombo.Items.Add(MakeColorChoice("Kem", ParseHex("#F6E7C1")));
                TextColorCombo.Items.Add(MakeColorChoice("Ivory", ParseHex("#FFF8E7")));
                TextColorCombo.Items.Add(MakeColorChoice("Gold", ParseHex("#E6C36A")));
                TextColorCombo.Items.Add(MakeColorChoice("Sand", ParseHex("#D4B896")));
                TextColorCombo.Items.Add(MakeColorChoice("Red", Colors.Red));
                TextColorCombo.Items.Add(MakeColorChoice("Crimson", ParseHex("#C41E3A")));
                TextColorCombo.Items.Add(MakeColorChoice("Coral", ParseHex("#E36A5D")));
                TextColorCombo.Items.Add(MakeColorChoice("Orange", ParseHex("#E07A2F")));
                TextColorCombo.Items.Add(MakeColorChoice("Amber", ParseHex("#D4A017")));
                TextColorCombo.Items.Add(MakeColorChoice("Blue", Colors.Blue));
                TextColorCombo.Items.Add(MakeColorChoice("Sky", ParseHex("#4A90C8")));
                TextColorCombo.Items.Add(MakeColorChoice("Indigo", ParseHex("#3D348B")));
                TextColorCombo.Items.Add(MakeColorChoice("Violet", ParseHex("#7C5CBF")));
                TextColorCombo.Items.Add(MakeColorChoice("Pink", ParseHex("#D97BA0")));
                TextColorCombo.Items.Add(MakeColorChoice("Sage", ParseHex("#8FA882")));
                TextColorCombo.Items.Add(MakeColorChoice("Mint", ParseHex("#7BC4A8")));
            }

            if (BackgroundCombo != null)
            {
                BackgroundCombo.Items.Clear();
                foreach (var swatch in PaperSwatches)
                {
                    BackgroundCombo.Items.Add(MakeColorChoice(swatch[0], ParseHex(swatch[1])));
                }
            }
        }

        private static ColorChoice MakeColorChoice(string name, Color color)
        {
            return new ColorChoice
            {
                Name = name,
                Preview = new SolidColorBrush(color)
            };
        }

        private static void SelectComboItem(ComboBox combo, string value)
        {
            if (combo == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private async Task LoadDatabaseAsync()
        {
            var shelves = await _dbService.GetLibraryTreeAsync();
            Libraries.Clear();
            Topics.Clear();
            foreach (var shelf in shelves)
            {
                Libraries.Add(shelf);
                foreach (var topic in shelf.Topics)
                {
                    Topics.Add(topic);
                }
            }

            if (Libraries.Count == 0)
            {
                var shelf = new LibraryShelf { Name = "Thư viện", SortIndex = 0 };
                await _dbService.SaveLibraryAsync(shelf);
                Libraries.Add(shelf);
            }

            if (Topics.Count == 0)
            {
                await CreateCanvasInShelfAsync(Libraries[0], "Chủ đề mới", "Bắt đầu viết...");
            }

            _currentLibrary = Libraries[0];
            _expandedShelves.Add(_currentLibrary.Id);
            if (Topics.Count > 0)
            {
                _currentTopic = Topics[0];
            }

            _currentPageIndex = 0;
            await EnsureTrailingEmptyScreenAsync();
            RebuildLibrarySidebar();
            RebuildPager();
        }

        private void SelectCanvas(Topic topic)
        {
            if (topic == null)
            {
                return;
            }

            _currentTopic = topic;
            foreach (var shelf in Libraries)
            {
                if (shelf.Topics.Contains(topic))
                {
                    _currentLibrary = shelf;
                    _expandedShelves.Add(shelf.Id);
                    break;
                }
            }

            _currentPageIndex = 0;
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                await EnsureTrailingEmptyScreenAsync();
                RebuildLibrarySidebar();
                RebuildPager();
            });
        }

        private void RebuildLibrarySidebar()
        {
            if (LibraryHost == null)
            {
                return;
            }

            LibraryHost.Children.Clear();
            string query = _libraryQuery ?? "";
            bool any = false;
            foreach (var shelf in Libraries)
            {
                var control = BuildShelfControl(shelf, query);
                if (control != null)
                {
                    LibraryHost.Children.Add(control);
                    any = true;
                }
            }

            if (!any && !string.IsNullOrWhiteSpace(query))
            {
                LibraryHost.Children.Add(new TextBlock
                {
                    Text = "Không tìm thấy kệ hoặc canvas.",
                    Margin = new Thickness(8, 12, 8, 0),
                    FontSize = 13,
                    Style = Resources["LibraryTertiaryTextStyle"] as Style
                });
            }
        }

        private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _libraryQuery = LibrarySearchBox?.Text ?? "";
            RebuildLibrarySidebar();
        }

        private FrameworkElement BuildShelfControl(LibraryShelf shelf, string query)
        {
            bool nameMatch = MatchesText(shelf.Name, query);
            var visibleTopics = new List<Topic>();
            foreach (var topic in shelf.Topics)
            {
                if (nameMatch || TopicMatchesQuery(topic, query))
                {
                    visibleTopics.Add(topic);
                }
            }

            if (!string.IsNullOrWhiteSpace(query) && !nameMatch && visibleTopics.Count == 0)
            {
                return null;
            }

            bool expanded = _expandedShelves.Contains(shelf.Id) ||
                            (_currentTopic != null && shelf.Topics.Contains(_currentTopic)) ||
                            !string.IsNullOrWhiteSpace(query);

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevron = new TextBlock
            {
                Text = expanded ? "▾" : "▸",
                FontSize = 12,
                Width = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                Style = Resources["LibraryPrimaryTextStyle"] as Style
            };
            var title = new TextBlock
            {
                Text = shelf.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Style = Resources["LibraryPrimaryTextStyle"] as Style
            };
            var more = CreateMoreButton();
            more.Flyout = BuildShelfMenu(shelf);
            var count = new TextBlock
            {
                Text = shelf.CanvasCount + " canvas",
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Style = Resources["LibraryTertiaryTextStyle"] as Style
            };
            header.Children.Add(chevron);
            Grid.SetColumn(title, 1);
            header.Children.Add(title);
            Grid.SetColumn(more, 2);
            header.Children.Add(more);
            Grid.SetColumn(count, 3);
            header.Children.Add(count);

            var canvases = new StackPanel
            {
                Spacing = 6,
                Margin = new Thickness(0, 4, 0, 4),
                Visibility = expanded ? Visibility.Visible : Visibility.Collapsed
            };
            foreach (var topic in visibleTopics)
            {
                canvases.Children.Add(BuildCanvasCard(shelf, topic));
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                canvases.Children.Add(BuildAddCanvasPlaceholder(shelf));
            }

            var headerChrome = new Border
            {
                Child = header,
                Style = Resources["LibraryShelfHeaderStyle"] as Style
            };
            headerChrome.Tapped += (s, e) =>
            {
                if (IsInsideButton(e.OriginalSource))
                {
                    return;
                }

                e.Handled = true;
                bool nowOpen = canvases.Visibility != Visibility.Visible;
                canvases.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                chevron.Text = nowOpen ? "▾" : "▸";
                if (nowOpen)
                {
                    _expandedShelves.Add(shelf.Id);
                }
                else
                {
                    _expandedShelves.Remove(shelf.Id);
                }
            };

            var root = new StackPanel { Spacing = 0 };
            root.Children.Add(headerChrome);
            root.Children.Add(canvases);
            return root;
        }

        private FrameworkElement BuildCanvasCard(LibraryShelf shelf, Topic topic)
        {
            bool selected = _currentTopic != null && _currentTopic.Id == topic.Id;
            var card = new Border
            {
                Style = Resources[selected ? "LibraryCanvasCardSelectedStyle" : "LibraryCanvasCardStyle"] as Style
            };
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = topic.Title,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Style = Resources["LibraryPrimaryTextStyle"] as Style
            });
            if (!string.IsNullOrWhiteSpace(topic.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = topic.Description,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Style = Resources["LibrarySecondaryTextStyle"] as Style
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = topic.PageCount + " màn hình",
                FontSize = 11,
                Style = Resources["LibraryTertiaryTextStyle"] as Style
            });

            var more = CreateMoreButton();
            more.VerticalAlignment = VerticalAlignment.Top;
            more.Flyout = BuildCanvasMenu(shelf, topic);

            Grid.SetColumn(stack, 0);
            layout.Children.Add(stack);
            Grid.SetColumn(more, 1);
            layout.Children.Add(more);
            card.Child = layout;
            card.Tapped += (s, e) =>
            {
                if (IsInsideButton(e.OriginalSource))
                {
                    return;
                }

                e.Handled = true;
                _currentLibrary = shelf;
                SelectCanvas(topic);
            };
            return card;
        }

        private FrameworkElement BuildAddCanvasPlaceholder(LibraryShelf shelf)
        {
            var add = new Button
            {
                Content = "+ Thêm canvas",
                Style = Resources["LibraryGhostCardStyle"] as Style,
                Margin = new Thickness(12, 0, 0, 0)
            };
            add.Click += async (s, e) => await AddCanvasToShelfAsync(shelf);
            add.Tapped += (s, e) => e.Handled = true;
            return add;
        }

        private static bool IsInsideButton(object source)
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is Button)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private Button CreateMoreButton()
        {
            var more = new Button
            {
                Content = "⋯",
                Style = Resources["LibraryAddCanvasButtonStyle"] as Style,
                FontSize = 14
            };
            ToolTipService.SetToolTip(more, "Tùy chọn");
            more.Tapped += (s, e) => e.Handled = true;
            return more;
        }

        private MenuFlyout BuildShelfMenu(LibraryShelf shelf)
        {
            var flyout = new MenuFlyout();
            var rename = new MenuFlyoutItem { Text = "Đổi tên kệ" };
            rename.Click += async (s, e) => await RenameShelfAsync(shelf);
            var remove = new MenuFlyoutItem { Text = "Xóa kệ" };
            remove.Click += async (s, e) => await DeleteShelfAsync(shelf);
            flyout.Items.Add(rename);
            flyout.Items.Add(remove);
            return flyout;
        }

        private MenuFlyout BuildCanvasMenu(LibraryShelf shelf, Topic topic)
        {
            var flyout = new MenuFlyout();
            var edit = new MenuFlyoutItem { Text = "Sửa canvas" };
            edit.Click += async (s, e) => await EditCanvasAsync(topic);
            var remove = new MenuFlyoutItem { Text = "Xóa canvas" };
            remove.Click += async (s, e) => await DeleteCanvasAsync(shelf, topic);
            flyout.Items.Add(edit);
            flyout.Items.Add(remove);
            return flyout;
        }

        private async void NewShelfButton_Click(object sender, RoutedEventArgs e)
        {
            string name = await PromptAsync("Kệ mới", "Tên kệ", "Kệ " + (Libraries.Count + 1));
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var shelf = new LibraryShelf { Name = name.Trim(), SortIndex = Libraries.Count };
            await _dbService.SaveLibraryAsync(shelf);
            Libraries.Add(shelf);
            _currentLibrary = shelf;
            _expandedShelves.Add(shelf.Id);
            RebuildLibrarySidebar();
        }

        private async void NewTopicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLibrary == null && Libraries.Count > 0)
            {
                _currentLibrary = Libraries[0];
            }

            await AddCanvasToShelfAsync(_currentLibrary);
        }

        private async Task AddCanvasToShelfAsync(LibraryShelf shelf)
        {
            if (shelf == null)
            {
                return;
            }

            string[] fields = await PromptCanvasAsync(
                "Canvas mới",
                "Chủ đề " + (Topics.Count + 1),
                "");
            if (fields == null || string.IsNullOrWhiteSpace(fields[0]))
            {
                return;
            }

            string description = string.IsNullOrWhiteSpace(fields[1]) ? "Bắt đầu viết..." : fields[1].Trim();
            _currentLibrary = shelf;
            var topic = await CreateCanvasInShelfAsync(shelf, fields[0].Trim(), description);
            _expandedShelves.Add(shelf.Id);
            SelectCanvas(topic);
        }

        private async Task RenameShelfAsync(LibraryShelf shelf)
        {
            if (shelf == null)
            {
                return;
            }

            string name = await PromptAsync("Đổi tên kệ", "Tên kệ", shelf.Name);
            if (string.IsNullOrWhiteSpace(name) || name.Trim() == shelf.Name)
            {
                return;
            }

            shelf.Name = name.Trim();
            await _dbService.SaveLibraryAsync(shelf);
            RebuildLibrarySidebar();
        }

        private async Task DeleteShelfAsync(LibraryShelf shelf)
        {
            if (shelf == null)
            {
                return;
            }

            string detail = shelf.CanvasCount == 0
                ? "Xóa kệ \"" + shelf.Name + "\"?"
                : "Xóa kệ \"" + shelf.Name + "\" và " + shelf.CanvasCount + " canvas bên trong?";
            if (!await ConfirmAsync("Xóa kệ", detail))
            {
                return;
            }

            bool wasCurrent = _currentLibrary == shelf ||
                              (_currentTopic != null && shelf.Topics.Contains(_currentTopic));
            await _dbService.DeleteLibraryAsync(shelf);
            for (int i = shelf.Topics.Count - 1; i >= 0; i--)
            {
                Topics.Remove(shelf.Topics[i]);
            }

            Libraries.Remove(shelf);
            _expandedShelves.Remove(shelf.Id);

            if (wasCurrent)
            {
                _currentLibrary = Libraries.Count > 0 ? Libraries[0] : null;
                Topic next = null;
                if (_currentLibrary != null && _currentLibrary.Topics.Count > 0)
                {
                    next = _currentLibrary.Topics[0];
                }
                else if (Topics.Count > 0)
                {
                    next = Topics[0];
                }

                if (next != null)
                {
                    SelectCanvas(next);
                    return;
                }

                _currentTopic = null;
                _currentPageIndex = 0;
            }

            RebuildLibrarySidebar();
            RebuildPager();
        }

        private async Task EditCanvasAsync(Topic topic)
        {
            if (topic == null)
            {
                return;
            }

            string[] fields = await PromptCanvasAsync("Sửa canvas", topic.Title, topic.Description);
            if (fields == null || string.IsNullOrWhiteSpace(fields[0]))
            {
                return;
            }

            topic.Title = fields[0].Trim();
            topic.Description = fields[1] == null ? "" : fields[1].Trim();
            await _dbService.SaveTopicAsync(topic);
            RebuildLibrarySidebar();
        }

        private async Task DeleteCanvasAsync(LibraryShelf shelf, Topic topic)
        {
            if (topic == null)
            {
                return;
            }

            if (!await ConfirmAsync("Xóa canvas", "Xóa \"" + topic.Title + "\" và mọi màn hình của canvas này?"))
            {
                return;
            }

            bool wasCurrent = _currentTopic != null && _currentTopic.Id == topic.Id;
            await _dbService.DeleteTopicAsync(topic);
            if (shelf != null)
            {
                shelf.Topics.Remove(topic);
            }

            Topics.Remove(topic);
            if (!wasCurrent)
            {
                RebuildLibrarySidebar();
                return;
            }

            Topic next = null;
            if (shelf != null && shelf.Topics.Count > 0)
            {
                next = shelf.Topics[0];
            }

            if (next == null && Topics.Count > 0)
            {
                next = Topics[0];
            }

            if (next != null)
            {
                SelectCanvas(next);
                return;
            }

            _currentTopic = null;
            _currentPageIndex = 0;
            RebuildLibrarySidebar();
            RebuildPager();
        }

        private async Task<Topic> CreateCanvasInShelfAsync(LibraryShelf shelf, string title, string description)
        {
            var topic = new Topic
            {
                Title = title,
                Description = description,
                LibraryId = shelf.Id
            };
            await _dbService.SaveTopicAsync(topic);
            var page = new CanvasPage { TopicId = topic.Id, PageIndex = 0, ApplyToAll = false };
            CopyStyleToPage(page);
            await _dbService.SavePageAsync(page);
            topic.Pages.Add(page);
            shelf.Topics.Insert(0, topic);
            Topics.Insert(0, topic);
            return topic;
        }

        private static bool TopicMatchesQuery(Topic topic, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            if (MatchesText(topic.Title, query) || MatchesText(topic.Description, query))
            {
                return true;
            }

            if (topic.Pages == null)
            {
                return false;
            }

            for (int i = 0; i < topic.Pages.Count; i++)
            {
                if (MatchesText(topic.Pages[i].Text, query))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesText(string value, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static bool IsBlankPage(CanvasPage page)
        {
            return page == null || string.IsNullOrWhiteSpace(page.Text);
        }

        private async Task<bool> EnsureTrailingEmptyScreenAsync()
        {
            if (_currentTopic == null)
            {
                return false;
            }

            if (_currentTopic.Pages.Count > 0 && IsBlankPage(_currentTopic.Pages[_currentTopic.Pages.Count - 1]))
            {
                return false;
            }

            var page = new CanvasPage
            {
                TopicId = _currentTopic.Id,
                PageIndex = _currentTopic.Pages.Count,
                ApplyToAll = false
            };
            CopyStyleToPage(page);
            await _dbService.SavePageAsync(page);
            _currentTopic.Pages.Add(page);
            return true;
        }

        private async Task InsertScreenAfterAsync(int index)
        {
            if (_currentTopic == null || index < 0 || index >= _currentTopic.Pages.Count)
            {
                return;
            }

            if (index == _currentTopic.Pages.Count - 1 && IsBlankPage(_currentTopic.Pages[index]))
            {
                await GoToPageAsync(index, false);
                return;
            }

            var page = new CanvasPage
            {
                TopicId = _currentTopic.Id,
                PageIndex = index + 1,
                ApplyToAll = false
            };
            CopyStyleToPage(page);
            await _dbService.SavePageAsync(page);
            _currentTopic.Pages.Insert(index + 1, page);
            await ReindexPagesAsync();
            await EnsureTrailingEmptyScreenAsync();
            _currentPageIndex = index + 1;
            RebuildPager();
            RebuildLibrarySidebar();
        }

        private async Task DeleteScreenAsync(int index)
        {
            if (_currentTopic == null || index < 0 || index >= _currentTopic.Pages.Count)
            {
                return;
            }

            var page = _currentTopic.Pages[index];
            if (_currentTopic.Pages.Count == 1)
            {
                if (IsBlankPage(page))
                {
                    return;
                }

                if (!await ConfirmAsync("Xóa màn hình", "Xóa nội dung màn hình này?"))
                {
                    return;
                }

                page.Text = "";
                page.WordDecorJson = "";
                page.DecorativeMode = false;
                await _dbService.SavePageAsync(page);
                RebuildPager();
                return;
            }

            if (!IsBlankPage(page) &&
                !await ConfirmAsync("Xóa màn hình", "Xóa màn hình " + (index + 1) + " khỏi canvas này?"))
            {
                return;
            }

            await _dbService.DeletePageAsync(page);
            _currentTopic.Pages.RemoveAt(index);
            await ReindexPagesAsync();
            if (_currentPageIndex >= _currentTopic.Pages.Count)
            {
                _currentPageIndex = Math.Max(0, _currentTopic.Pages.Count - 1);
            }
            else if (_currentPageIndex > index)
            {
                _currentPageIndex--;
            }

            await EnsureTrailingEmptyScreenAsync();
            RebuildPager();
            RebuildLibrarySidebar();
        }

        private async Task ReindexPagesAsync()
        {
            if (_currentTopic == null)
            {
                return;
            }

            for (int i = 0; i < _currentTopic.Pages.Count; i++)
            {
                if (_currentTopic.Pages[i].PageIndex != i)
                {
                    _currentTopic.Pages[i].PageIndex = i;
                    await _dbService.SavePageAsync(_currentTopic.Pages[i]);
                }
            }
        }

        private async Task MaybeEnsureTrailingEmptyScreenAsync(CanvasPage page)
        {
            if (_currentTopic == null || page == null || string.IsNullOrWhiteSpace(page.Text))
            {
                return;
            }

            if (_currentTopic.Pages.Count == 0 ||
                _currentTopic.Pages[_currentTopic.Pages.Count - 1] != page)
            {
                return;
            }

            if (!await EnsureTrailingEmptyScreenAsync())
            {
                return;
            }

            if (CarouselTrack != null)
            {
                CarouselTrack.Children.Add(CreateSlide(_currentTopic.Pages[_currentTopic.Pages.Count - 1]));
                RelayoutCarousel();
            }

            RebuildLibrarySidebar();
        }

        private async Task<string> PromptAsync(string title, string placeholder, string initial)
        {
            var box = new TextBox { Text = initial, PlaceholderText = placeholder };
            var dialog = new ContentDialog
            {
                Title = title,
                Content = box,
                PrimaryButtonText = "OK",
                CloseButtonText = "Hủy",
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return box.Text;
        }

        private async Task<string[]> PromptCanvasAsync(string heading, string initialTitle, string initialDescription)
        {
            var titleBox = new TextBox
            {
                Text = initialTitle ?? "",
                PlaceholderText = "Chủ đề của canvas",
                Header = "Chủ đề"
            };
            var descriptionBox = new TextBox
            {
                Text = initialDescription ?? "",
                PlaceholderText = "Mô tả ngắn",
                Header = "Mô tả"
            };
            var stack = new StackPanel { Spacing = 12 };
            stack.Children.Add(titleBox);
            stack.Children.Add(descriptionBox);
            var dialog = new ContentDialog
            {
                Title = heading,
                Content = stack,
                PrimaryButtonText = "Lưu",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return new[] { titleBox.Text, descriptionBox.Text };
        }

        private async Task<bool> ConfirmAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "Xóa",
                CloseButtonText = "Hủy",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private void RebuildPager()
        {
            if (CarouselTrack == null)
            {
                return;
            }

            if (_currentTopic == null)
            {
                _isUpdatingPager = true;
                CarouselTrack.Children.Clear();
                _slides.Clear();
                _currentEditor = null;
                _currentRoot = null;
                _currentTextContainer = null;
                UpdatePageIndicator();
                var done = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    _isUpdatingPager = false;
                });
                return;
            }

            _isUpdatingPager = true;
            CarouselTrack.Children.Clear();
            _slides.Clear();
            foreach (var page in _currentTopic.Pages)
            {
                CarouselTrack.Children.Add(CreateSlide(page));
            }

            if (_currentPageIndex < 0) _currentPageIndex = 0;
            if (_currentPageIndex >= _slides.Count) _currentPageIndex = Math.Max(0, _slides.Count - 1);

            RelayoutCarousel();
            BindCurrentSlide();
            ScrollToIndex(_currentPageIndex, false);
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                _isUpdatingPager = false;
            });
        }

        private FrameworkElement CreateSlide(CanvasPage page)
        {
            const double canvasWidth = 1080;
            const double canvasHeight = 1920;
            double padY = canvasHeight * 0.075;
            double padX = canvasWidth * 0.07;

            var pageStyle = StyleFromPage(page);
            var lockedFace = CanvasTypefaces.ToXaml(pageStyle.FontFamily, pageStyle.FontWeight);
            var editor = new Editor
            {
                Style = Resources["CanvasEditorStyle"] as Style,
                FontFamily = lockedFace,
                FontSize = ReadBaseSize(pageStyle),
                FontWeight = pageStyle.FontWeight == "Bold" ? FontWeights.Bold
                           : pageStyle.FontWeight == "SemiBold" ? FontWeights.SemiBold
                           : FontWeights.Normal,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(ResolveColor(pageStyle.TextColor)),
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                IsSpellCheckEnabled = false,
                UseSystemFocusVisuals = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = canvasHeight - (padY * 2)
            };
            editor.ApplySelectionTint(
                ResolveColor(CanvasContrast.InkFor(pageStyle.BackgroundColor, pageStyle.TextColor)),
                ParseHex(pageStyle.BackgroundColor));
            editor.TextChanged += CanvasEditor_TextChanged;
            editor.Tag = page;
            ScrollViewer.SetVerticalScrollMode(editor, ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollMode(editor, ScrollMode.Disabled);

            var highlightLayer = new Canvas
            {
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            var decorHost = new Canvas
            {
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };

            var layer = new Grid();
            layer.Children.Add(highlightLayer);
            layer.Children.Add(editor);
            layer.Children.Add(decorHost);

            var textContainer = new Border
            {
                Child = layer,
                Margin = new Thickness(padX, padY, padX, padY),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Colors.Transparent)
            };

            var root = new Border
            {
                Width = canvasWidth,
                Height = canvasHeight,
                CornerRadius = new CornerRadius(28),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
                Opacity = 0.8
            };
            var stage = new Grid();
            stage.Children.Add(textContainer);
            root.Child = stage;

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = root,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var host = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch
            };
            host.Children.Add(viewbox);

            var visual = new SlideVisual
            {
                Page = page,
                Host = host,
                Root = root,
                TextContainer = textContainer,
                Editor = editor,
                DecorHost = decorHost,
                HighlightLayer = highlightLayer,
                Words = new List<WordDecor>(),
                WordChips = new List<Border>()
            };
            host.Tag = visual;
            root.Tag = visual;
            stage.Children.Add(BuildPageIndexBlob(visual));
            stage.Children.Add(BuildCanvasFooterBlob(visual, true));
            host.Children.Add(BuildSlideCornerChrome(visual));
            editor.Loaded += (s, e) =>
            {
                LockEditorTypeface(editor, pageStyle);
                RefreshDecorOverlay(visual);
                ScheduleFitToViewport(editor);
            };
            editor.SizeChanged += (s, e) =>
            {
                if (_isFitting || _isUpdatingEditor)
                {
                    return;
                }

                if (e.NewSize.Width > 8 && e.NewSize != e.PreviousSize)
                {
                    RefreshDecorOverlay(visual);
                }
            };
            _slides.Add(visual);
            RefreshSlideMeta();

            host.Tapped += async (s, e) =>
            {
                if (IsInsideButton(e.OriginalSource))
                {
                    return;
                }

                int index = _slides.IndexOf(visual);
                if (index >= 0 && index != _currentPageIndex)
                {
                    e.Handled = true;
                    await GoToPageAsync(index, false);
                }
            };

            _isUpdatingEditor = true;
            LockEditorTypeface(editor, pageStyle);
            editor.Document.SetText(TextSetOptions.None, NormalizeRichText(page.Text));
            ApplyFormattingToEditor(editor, pageStyle);
            LockEditorTypeface(editor, pageStyle);
            ApplySlideChrome(root, textContainer, pageStyle);
            visual.FittedWordCount = CountWords(page.Text);
            SetSlideDecorative(visual, page.DecorativeMode);
            _isUpdatingEditor = false;
            return host;
        }

        private FrameworkElement BuildSlideCornerChrome(SlideVisual visual)
        {
            var deleteBtn = new Button
            {
                Content = "×",
                Width = 28,
                Height = 28,
                Style = Resources["LibraryCornerButtonStyle"] as Style
            };
            ToolTipService.SetToolTip(deleteBtn, "Xóa màn hình");
            deleteBtn.Click += async (s, e) =>
            {
                int index = _slides.IndexOf(visual);
                if (index >= 0)
                {
                    await DeleteScreenAsync(index);
                }
            };
            deleteBtn.Tapped += (s, e) => e.Handled = true;

            var addBtn = new Button
            {
                Content = "+",
                Width = 28,
                Height = 28,
                Style = Resources["LibraryCornerButtonStyle"] as Style
            };
            ToolTipService.SetToolTip(addBtn, "Thêm màn hình bên phải");
            addBtn.Click += async (s, e) =>
            {
                int index = _slides.IndexOf(visual);
                if (index >= 0)
                {
                    await InsertScreenAfterAsync(index);
                }
            };
            addBtn.Tapped += (s, e) => e.Handled = true;

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                IsHitTestVisible = true
            };
            bar.Children.Add(addBtn);
            bar.Children.Add(deleteBtn);

            bar.Visibility = Visibility.Collapsed;
            visual.Host.PointerEntered += (s, e) => bar.Visibility = Visibility.Visible;
            visual.Host.PointerExited += (s, e) =>
            {
                var point = e.GetCurrentPoint(visual.Host).Position;
                bool inside = point.X >= 0 && point.Y >= 0 &&
                              point.X <= visual.Host.ActualWidth && point.Y <= visual.Host.ActualHeight;
                if (!inside)
                {
                    bar.Visibility = Visibility.Collapsed;
                }
            };
            return bar;
        }

        private FrameworkElement BuildPageIndexBlob(SlideVisual visual)
        {
            var number = new TextBlock
            {
                FontSize = 46,
                FontWeight = FontWeights.SemiBold,
                FontFamily = CanvasTypefaces.ToXaml("Be Vietnam Pro", "SemiBold"),
                CharacterSpacing = 40
            };
            var total = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                FontFamily = CanvasTypefaces.ToXaml("Be Vietnam Pro", "SemiBold"),
                Opacity = 0.72,
                Margin = new Thickness(2, -4, 0, 0)
            };
            visual.PageNumberText = number;
            visual.PageTotalText = total;

            var labels = new StackPanel { Spacing = 0 };
            labels.Children.Add(number);
            labels.Children.Add(total);

            var path = new Windows.UI.Xaml.Shapes.Path
            {
                Data = MakeCornerBlobGeometry(true),
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            visual.PageBlobPath = path;

            var host = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0),
                MinWidth = 148,
                MinHeight = 108,
                IsHitTestVisible = false
            };
            host.Children.Add(path);
            host.Children.Add(new Border
            {
                Child = labels,
                Padding = new Thickness(28, 22, 38, 26),
                Background = new SolidColorBrush(Colors.Transparent)
            });
            return host;
        }

        private FrameworkElement BuildCanvasFooterBlob(SlideVisual visual, bool interactive)
        {
            string paper = visual?.Page != null
                ? StyleFromPage(visual.Page).BackgroundColor
                : _settings.BackgroundColor;
            return BuildCanvasFooterBlob(
                visual,
                CanvasTitleText(),
                AlbumNameText(),
                paper,
                interactive);
        }

        private FrameworkElement BuildCanvasFooterBlob(
            SlideVisual visual,
            string canvasTitle,
            string albumName,
            string paperHex,
            bool interactive)
        {
            var title = new TextBlock
            {
                Text = canvasTitle,
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                FontFamily = CanvasTypefaces.ToXaml("Be Vietnam Pro", "SemiBold"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Right,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (visual != null)
            {
                visual.FooterTitleText = title;
            }

            FrameworkElement album;
            if (interactive)
            {
                var link = new HyperlinkButton
                {
                    Content = albumName,
                    FontSize = 20,
                    FontFamily = CanvasTypefaces.ToXaml("Be Vietnam Pro", "SemiBold"),
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 6, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Background = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    UseSystemFocusVisuals = false
                };
                ToolTipService.SetToolTip(link, "Mở album " + albumName);
                link.Click += (s, e) => RevealAlbum();
                album = link;
                if (visual != null)
                {
                    visual.FooterAlbumLink = link;
                }
            }
            else
            {
                var caption = new TextBlock
                {
                    Text = albumName,
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = CanvasTypefaces.ToXaml("Be Vietnam Pro", "SemiBold"),
                    TextAlignment = TextAlignment.Right,
                    TextDecorations = Windows.UI.Text.TextDecorations.Underline,
                    Opacity = 0.88,
                    Margin = new Thickness(0, 6, 0, 0)
                };
                album = caption;
                if (visual != null)
                {
                    visual.FooterAlbumLink = caption;
                }
            }

            var labels = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right
            };
            labels.Children.Add(title);
            labels.Children.Add(album);

            var path = new Windows.UI.Xaml.Shapes.Path
            {
                Data = MakeCornerBlobGeometry(false),
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            if (visual != null)
            {
                visual.FooterBlobPath = path;
            }

            var host = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0),
                MinWidth = 220,
                MaxWidth = 560,
                MinHeight = 108
            };
            host.Children.Add(path);
            host.Children.Add(new Border
            {
                Child = labels,
                Padding = new Thickness(40, 24, 30, 26),
                Background = new SolidColorBrush(Colors.Transparent)
            });
            PaintCornerBlob(path, title, album, paperHex);
            return host;
        }

        private static PathGeometry MakeCornerBlobGeometry(bool topLeft)
        {
            var figure = new PathFigure { IsFilled = true, IsClosed = true };
            if (topLeft)
            {
                figure.StartPoint = new Point(0, 0);
                figure.Segments.Add(new LineSegment { Point = new Point(1, 0) });
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = new Point(1.02, 0.42),
                    Point2 = new Point(0.86, 0.92),
                    Point3 = new Point(0.52, 1)
                });
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = new Point(0.22, 1.06),
                    Point2 = new Point(0.02, 0.78),
                    Point3 = new Point(0, 0.58)
                });
            }
            else
            {
                figure.StartPoint = new Point(1, 1);
                figure.Segments.Add(new LineSegment { Point = new Point(0, 1) });
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = new Point(-0.02, 0.58),
                    Point2 = new Point(0.14, 0.08),
                    Point3 = new Point(0.48, 0)
                });
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = new Point(0.78, -0.06),
                    Point2 = new Point(0.98, 0.22),
                    Point3 = new Point(1, 0.42)
                });
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private void RefreshSlideMeta()
        {
            int total = Math.Max(1, _slides.Count);
            string canvasTitle = CanvasTitleText();
            string albumName = AlbumNameText();
            for (int i = 0; i < _slides.Count; i++)
            {
                var visual = _slides[i];
                if (visual.PageNumberText != null)
                {
                    visual.PageNumberText.Text = (i + 1).ToString("00", CultureInfo.InvariantCulture);
                }

                if (visual.PageTotalText != null)
                {
                    visual.PageTotalText.Text = "/ " + total.ToString(CultureInfo.InvariantCulture);
                }

                if (visual.FooterTitleText != null)
                {
                    visual.FooterTitleText.Text = canvasTitle;
                }

                if (visual.FooterAlbumLink is HyperlinkButton link)
                {
                    link.Content = albumName;
                }
                else if (visual.FooterAlbumLink is TextBlock caption)
                {
                    caption.Text = albumName;
                }

                string paper = visual.Page != null ? StyleFromPage(visual.Page).BackgroundColor : _settings.BackgroundColor;
                PaintSlideMeta(visual, paper);
            }
        }

        private void PaintSlideMeta(SlideVisual visual, string paperHex)
        {
            if (visual == null)
            {
                return;
            }

            PaintCornerBlob(visual.PageBlobPath, visual.PageNumberText, visual.PageTotalText, paperHex);
            PaintCornerBlob(visual.FooterBlobPath, visual.FooterTitleText, visual.FooterAlbumLink, paperHex);
        }

        private static void PaintCornerBlob(Windows.UI.Xaml.Shapes.Path blob, FrameworkElement labelA, FrameworkElement labelB, string paperHex)
        {
            Color paper = ParseHex(paperHex);
            bool darkPaper = CanvasContrast.RelativeLuminance(paper) < 0.38;
            Color wash = darkPaper
                ? Color.FromArgb(235, 246, 231, 193)
                : Color.FromArgb(235, 38, 24, 14);
            Color ink = CanvasContrast.BestInkOn(CanvasContrast.Composite(wash, paper));
            if (blob != null)
            {
                blob.Fill = new SolidColorBrush(wash);
            }

            var brush = new SolidColorBrush(ink);
            ApplyMetaForeground(labelA, brush);
            ApplyMetaForeground(labelB, brush);
        }

        private static void ApplyMetaForeground(FrameworkElement element, Brush brush)
        {
            if (element is TextBlock block)
            {
                block.Foreground = brush;
            }
            else if (element is Control control)
            {
                control.Foreground = brush;
            }
        }

        private string CanvasTitleText()
        {
            if (_currentTopic != null && !string.IsNullOrWhiteSpace(_currentTopic.Title))
            {
                return _currentTopic.Title.Trim();
            }

            return "Canvas";
        }

        private string AlbumNameText()
        {
            var shelf = FindShelfForTopic(_currentTopic);
            if (shelf != null && !string.IsNullOrWhiteSpace(shelf.Name))
            {
                return shelf.Name.Trim();
            }

            return "Album";
        }

        private LibraryShelf FindShelfForTopic(Topic topic)
        {
            if (topic == null)
            {
                return _currentLibrary;
            }

            for (int i = 0; i < Libraries.Count; i++)
            {
                if (Libraries[i].Topics.Contains(topic))
                {
                    return Libraries[i];
                }
            }

            return _currentLibrary;
        }

        private void RevealAlbum()
        {
            var shelf = FindShelfForTopic(_currentTopic);
            if (shelf != null)
            {
                _currentLibrary = shelf;
                _expandedShelves.Add(shelf.Id);
            }

            RebuildLibrarySidebar();
        }

        private sealed class SlideVisual
        {
            public CanvasPage Page;
            public Grid Host;
            public Border Root;
            public Border TextContainer;
            public Editor Editor;
            public Canvas DecorHost;
            public Canvas HighlightLayer;
            public Windows.UI.Xaml.Shapes.Path PageBlobPath;
            public Windows.UI.Xaml.Shapes.Path FooterBlobPath;
            public TextBlock PageNumberText;
            public TextBlock PageTotalText;
            public TextBlock FooterTitleText;
            public FrameworkElement FooterAlbumLink;
            public List<WordDecor> Words;
            public List<Border> WordChips;
            public int FittedWordCount = -1;
            public int OverlayRetries;
        }

        private sealed class OverlayPack
        {
            public Editor Editor;
            public Canvas Highlights;
            public List<WordDecor> Words;
        }

        private SlideVisual GetSlideVisual(int index)
        {
            if (index < 0 || index >= _slides.Count)
            {
                return null;
            }

            return _slides[index];
        }

        private void CarouselHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RelayoutCarousel();
            ScrollToIndex(_currentPageIndex, false);
            UpdateSpotlightOpacity(_currentPageIndex);
            ScheduleFitToViewport(_currentEditor);
        }

        private void RelayoutCarousel()
        {
            if (CarouselScroller == null || CarouselTrack == null || _slides.Count == 0)
            {
                return;
            }

            double viewportW = CarouselScroller.ActualWidth;
            double viewportH = CarouselScroller.ActualHeight;
            if (viewportW < 8 || viewportH < 8)
            {
                return;
            }

            double cardHeight = viewportH * 0.84;
            _cardWidth = cardHeight * (1080.0 / 1920.0);
            _slotWidth = _cardWidth + 28;
            _sidePad = Math.Max(24, (viewportW - _slotWidth) / 2.0);
            CarouselTrack.Width = (_sidePad * 2.0) + (_slides.Count * _slotWidth);
            CarouselTrack.Height = viewportH;

            for (int i = 0; i < _slides.Count; i++)
            {
                var slide = _slides[i];
                slide.Host.Width = _cardWidth;
                slide.Host.Height = viewportH;
                Canvas.SetLeft(slide.Host, _sidePad + (i * _slotWidth) + (_slotWidth / 2.0) - (_cardWidth / 2.0));
                Canvas.SetTop(slide.Host, 0);
            }

            RefreshSlideMeta();
        }

        private void CarouselScroller_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_isUpdatingPager)
            {
                return;
            }

            UpdateSpotlightOpacity(NearestSlideIndex());
            if (e.IsIntermediate || _isCarouselSnapping || _slotWidth <= 0 || _slides.Count == 0)
            {
                return;
            }

            int nearest = NearestSlideIndex();
            if (nearest != _currentPageIndex)
            {
                var ignore = GoToPageAsync(nearest, false);
                return;
            }

            double target = nearest * _slotWidth;
            if (Math.Abs(CarouselScroller.HorizontalOffset - target) > 2)
            {
                ScrollToIndex(_currentPageIndex, true);
            }
        }

        private void ScrollToIndex(int index, bool animate)
        {
            if (CarouselScroller == null || _slotWidth <= 0 || _slides.Count == 0)
            {
                return;
            }

            index = Math.Max(0, Math.Min(_slides.Count - 1, index));
            double offset = index * _slotWidth;
            _isCarouselSnapping = true;
            int generation = ++_scrollGeneration;
            CarouselScroller.ChangeView(offset, 0, 1, !animate);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(animate ? 420 : 50) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (generation == _scrollGeneration)
                {
                    _isCarouselSnapping = false;
                }
            };
            timer.Start();
        }

        private void BindCurrentSlide()
        {
            var visual = GetSlideVisual(_currentPageIndex);
            _currentEditor = visual?.Editor;
            _currentRoot = visual?.Root;
            _currentTextContainer = visual?.TextContainer;

            if (visual?.Page != null)
            {
                ApplyStyleFromPage(visual.Page);
                ApplyCanvasBackground();
                ApplyRoundedRect();
            }

            for (int i = 0; i < _slides.Count; i++)
            {
                bool current = i == _currentPageIndex;
                bool decorative = _slides[i].Page != null && _slides[i].Page.DecorativeMode;
                _slides[i].Editor.IsHitTestVisible = current && !decorative;
                _slides[i].Editor.IsReadOnly = decorative;
                _slides[i].Editor.IsTabStop = current && !decorative;
                if (_slides[i].DecorHost != null)
                {
                    _slides[i].DecorHost.IsHitTestVisible = current && decorative;
                }
                if (!current && _slides[i].Page != null)
                {
                    _slides[i].Page.ApplyToAll = false;
                }
            }

            UpdateSpotlightOpacity(_currentPageIndex);
            UpdatePageIndicator();
            SyncApplyAllCheckbox();
            SyncDecorativeMode();
            if (visual?.Page == null || !visual.Page.DecorativeMode)
            {
                _currentEditor?.Focus(FocusState.Programmatic);
            }
        }

        private int NearestSlideIndex()
        {
            if (_slotWidth <= 0 || _slides.Count == 0)
            {
                return Math.Max(0, _currentPageIndex);
            }

            int nearest = (int)Math.Round(CarouselScroller.HorizontalOffset / _slotWidth);
            return Math.Max(0, Math.Min(_slides.Count - 1, nearest));
        }

        private void UpdateSpotlightOpacity(int focusIndex)
        {
            for (int i = 0; i < _slides.Count; i++)
            {
                _slides[i].Root.Opacity = i == focusIndex ? 1.0 : 0.8;
            }
        }

        private void SyncApplyAllCheckbox()
        {
            if (ApplyAllCheckBox == null)
            {
                return;
            }

            var page = GetSlideVisual(_currentPageIndex)?.Page;
            bool on = page != null && page.ApplyToAll;
            if (ApplyAllCheckBox.IsChecked == on)
            {
                return;
            }

            bool previous = _isLoadingSettings;
            _isLoadingSettings = true;
            ApplyAllCheckBox.IsChecked = on;
            _isLoadingSettings = previous;
        }

        private void ApplyAllCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            var page = GetSlideVisual(_currentPageIndex)?.Page;
            if (page == null)
            {
                return;
            }

            page.ApplyToAll = ApplyAllCheckBox.IsChecked == true;
        }

        private void SyncDecorativeMode()
        {
            var visual = GetSlideVisual(_currentPageIndex);
            bool on = visual?.Page != null && visual.Page.DecorativeMode;
            if (DecorativeModeCheckBox != null && DecorativeModeCheckBox.IsChecked != on)
            {
                bool previous = _isLoadingSettings;
                _isLoadingSettings = true;
                DecorativeModeCheckBox.IsChecked = on;
                _isLoadingSettings = previous;
            }

            if (WordHighlightCombo != null)
            {
                WordHighlightCombo.IsEnabled = on;
            }

            if (visual != null)
            {
                SetSlideDecorative(visual, on);
            }
        }

        private async void DecorativeModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            var visual = GetSlideVisual(_currentPageIndex);
            if (visual?.Page == null)
            {
                return;
            }

            visual.Page.DecorativeMode = DecorativeModeCheckBox.IsChecked == true;
            SetSlideDecorative(visual, visual.Page.DecorativeMode);
            if (WordHighlightCombo != null)
            {
                WordHighlightCombo.IsEnabled = visual.Page.DecorativeMode;
            }

            await _dbService.SavePageAsync(visual.Page);
        }

        private async void WordHighlightCombo_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || _isUpdatingEditor)
            {
                return;
            }

            await ApplyDecorToSelectedAsync();
        }

        private void SetSlideDecorative(SlideVisual visual, bool on)
        {
            if (visual == null)
            {
                return;
            }

            bool current = _slides.IndexOf(visual) == _currentPageIndex;
            if (visual.Editor != null)
            {
                visual.Editor.Visibility = Visibility.Visible;
                visual.Editor.IsReadOnly = on;
                visual.Editor.IsHitTestVisible = current && !on;
                visual.Editor.IsTabStop = current && !on;
            }

            if (visual.DecorHost != null)
            {
                visual.DecorHost.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                visual.DecorHost.IsHitTestVisible = current && on;
            }

            EnsureWordModel(visual);
            ApplyWordCharacterFormats(visual.Editor, visual.Page, visual.Words);
            bool showOverlay = on || HasWordHighlights(visual);
            if (visual.HighlightLayer != null)
            {
                visual.HighlightLayer.Visibility = showOverlay ? Visibility.Visible : Visibility.Collapsed;
            }

            if (on)
            {
                visual.OverlayRetries = 0;
                RefreshDecorOverlay(visual);
            }
            else
            {
                ClearWordSelection(visual);
                if (showOverlay)
                {
                    visual.OverlayRetries = 0;
                    RefreshDecorOverlay(visual);
                }
                else
                {
                    ClearDecorOverlay(visual);
                }
            }
        }

        private static bool HasWordHighlights(SlideVisual visual)
        {
            if (visual?.Words == null)
            {
                return false;
            }

            for (int i = 0; i < visual.Words.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(visual.Words[i].Highlight) && visual.Words[i].Highlight != "None")
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearWordSelection(SlideVisual visual)
        {
            if (visual?.Words == null)
            {
                return;
            }

            for (int i = 0; i < visual.Words.Count; i++)
            {
                visual.Words[i].Selected = false;
            }
        }

        private static void ClearDecorOverlay(SlideVisual visual)
        {
            visual?.HighlightLayer?.Children.Clear();
            visual?.DecorHost?.Children.Clear();
            if (visual != null)
            {
                visual.WordChips = new List<Border>();
            }
        }

        private void EnsureWordModel(SlideVisual visual)
        {
            if (visual?.Editor == null || visual.Page == null)
            {
                return;
            }

            visual.Words = BuildWordModel(visual.Editor, visual.Page);
        }

        private List<WordDecor> BuildWordModel(Editor editor, CanvasPage page)
        {
            var words = new List<WordDecor>();
            if (editor?.Document == null || page == null)
            {
                return words;
            }

            editor.Document.GetText(TextGetOptions.None, out string raw);
            CollectWordSpans(raw, words);
            var saved = ParseWordDecor(page.WordDecorJson);
            var settings = StyleFromPage(page);
            for (int i = 0; i < words.Count; i++)
            {
                var decor = words[i];
                if (i < saved.Count)
                {
                    if (!string.IsNullOrWhiteSpace(saved[i].Weight))
                    {
                        decor.Weight = saved[i].Weight;
                    }

                    if (!string.IsNullOrWhiteSpace(saved[i].Color))
                    {
                        decor.Color = saved[i].Color;
                    }

                    if (!string.IsNullOrWhiteSpace(saved[i].Highlight))
                    {
                        decor.Highlight = saved[i].Highlight;
                    }

                    decor.HasSaved = true;
                }
                else
                {
                    decor.Weight = settings.FontWeight;
                    decor.Color = settings.TextColor;
                    decor.Highlight = "None";
                }
            }

            return words;
        }

        private static void CollectWordSpans(string raw, List<WordDecor> words)
        {
            words.Clear();
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c == '\r' || c == '\n' || char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < raw.Length)
                {
                    char next = raw[i];
                    if (next == '\r' || next == '\n' || char.IsWhiteSpace(next))
                    {
                        break;
                    }

                    i++;
                }

                words.Add(new WordDecor
                {
                    Text = raw.Substring(start, i - start),
                    Start = start,
                    Length = i - start
                });
            }
        }

        private void ApplyWordCharacterFormats(Editor editor, CanvasPage page, List<WordDecor> words)
        {
            if (editor?.Document == null || page == null || words == null)
            {
                return;
            }

            bool previous = _isUpdatingEditor;
            _isUpdatingEditor = true;
            editor.BeginUpdate();
            try
            {
                for (int i = 0; i < words.Count; i++)
                {
                    var word = words[i];
                    if (!word.HasSaved || word.Length <= 0)
                    {
                        continue;
                    }

                    var range = editor.Document.GetRange(word.Start, word.Start + word.Length);
                    range.CharacterFormat.Weight = WeightValue(word.Weight);
                    range.CharacterFormat.ForegroundColor = CanvasContrast.InkOnHighlight(
                        ResolveColor(word.Color),
                        ParseHex(page.BackgroundColor),
                        word.Highlight);
                    range.CharacterFormat.BackgroundColor = Colors.Transparent;
                }

                editor.Document.Selection.SetRange(0, 0);
            }
            catch (ArgumentException)
            {
            }
            finally
            {
                editor.EndUpdate();
                _isUpdatingEditor = previous;
            }
        }

        private static int WeightValue(string weight)
        {
            return weight == "Bold" ? FontWeights.Bold.Weight
                 : weight == "SemiBold" ? FontWeights.SemiBold.Weight
                 : FontWeights.Normal.Weight;
        }

        private void RefreshDecorOverlay(SlideVisual visual)
        {
            if (visual?.Editor == null || visual.Page == null)
            {
                return;
            }

            bool decorative = visual.Page.DecorativeMode;
            if (!decorative && !HasWordHighlights(visual))
            {
                ClearDecorOverlay(visual);
                return;
            }

            if (visual.Words == null || visual.Words.Count == 0)
            {
                EnsureWordModel(visual);
            }

            int placed = LayoutDecorOverlay(
                visual.Editor,
                visual.HighlightLayer,
                decorative ? visual.DecorHost : null,
                visual.Words,
                decorative,
                visual);

            if (visual.Words != null && placed < visual.Words.Count && visual.OverlayRetries < 8)
            {
                visual.OverlayRetries++;
                var captured = visual;
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () => RefreshDecorOverlay(captured));
            }
        }

        private int LayoutDecorOverlay(
            Editor editor,
            Canvas highlights,
            Canvas hits,
            List<WordDecor> words,
            bool interactive,
            SlideVisual visual)
        {
            highlights?.Children.Clear();
            hits?.Children.Clear();
            if (visual != null)
            {
                visual.WordChips = new List<Border>();
            }

            if (hits == null && visual?.DecorHost != null)
            {
                visual.DecorHost.Children.Clear();
            }

            if (editor == null || words == null || words.Count == 0)
            {
                return 0;
            }

            editor.UpdateLayout();
            int placed = 0;
            for (int i = 0; i < words.Count; i++)
            {
                if (!TryGetWordRect(editor, words[i], out Rect rect))
                {
                    continue;
                }

                placed++;
                double inflateX = Math.Max(6, rect.Height * 0.12);
                double inflateY = Math.Max(3, rect.Height * 0.08);
                var box = new Rect(rect.X - inflateX, rect.Y - inflateY, rect.Width + (inflateX * 2), rect.Height + (inflateY * 2));

                if (highlights != null && words[i].Highlight != "None")
                {
                    var fill = new Border { IsHitTestVisible = false };
                    PlaceOverlayBorder(fill, box);
                    ApplyWordChrome(fill, words[i], false);
                    highlights.Children.Add(fill);
                }

                if (hits != null && interactive)
                {
                    var hit = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
                        Tag = i
                    };
                    PlaceOverlayBorder(hit, box);
                    ApplyHitRing(hit, words[i].Selected);
                    int index = i;
                    hit.Tapped += (s, e) =>
                    {
                        if (visual?.Page == null || !visual.Page.DecorativeMode)
                        {
                            return;
                        }

                        if (_slides.IndexOf(visual) != _currentPageIndex)
                        {
                            return;
                        }

                        e.Handled = true;
                        words[index].Selected = !words[index].Selected;
                        ApplyHitRing(hit, words[index].Selected);
                    };
                    hits.Children.Add(hit);
                    if (visual != null)
                    {
                        visual.WordChips.Add(hit);
                    }
                }
            }

            return placed;
        }

        private static void PlaceOverlayBorder(Border border, Rect box)
        {
            Canvas.SetLeft(border, box.X);
            Canvas.SetTop(border, box.Y);
            border.Width = Math.Max(8, box.Width);
            border.Height = Math.Max(8, box.Height);
        }

        private static bool TryGetWordRect(Editor editor, WordDecor word, out Rect rect)
        {
            rect = Rect.Empty;
            if (editor?.Document == null || word == null || word.Length <= 0)
            {
                return false;
            }

            try
            {
                var range = editor.Document.GetRange(word.Start, word.Start + word.Length);
                range.GetRect(PointOptions.ClientCoordinates, out rect, out int hit);
                if (rect.Width > 1 && rect.Height > 1)
                {
                    return true;
                }

                range.GetRect(PointOptions.ClientCoordinates | PointOptions.Transform, out rect, out hit);
                return rect.Width > 1 && rect.Height > 1;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static void ApplyHitRing(Border hit, bool selected)
        {
            hit.CornerRadius = new CornerRadius(6);
            hit.BorderThickness = new Thickness(selected ? 3 : 0);
            hit.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(230, 255, 196, 72)
                : Colors.Transparent);
        }

        private static void ApplyWordChrome(Border chip, WordDecor decor, bool selected)
        {
            string highlight = string.IsNullOrWhiteSpace(decor.Highlight) ? "None" : decor.Highlight;
            if (highlight == "Pill")
            {
                chip.CornerRadius = new CornerRadius(28);
                chip.Background = new SolidColorBrush(Color.FromArgb(80, 255, 214, 102));
                chip.BorderThickness = new Thickness(selected ? 3 : 0);
            }
            else if (highlight == "Marker")
            {
                chip.CornerRadius = new CornerRadius(4);
                chip.Background = new SolidColorBrush(Color.FromArgb(110, 255, 224, 90));
                chip.BorderThickness = new Thickness(selected ? 3 : 0);
            }
            else if (highlight == "Frame")
            {
                chip.CornerRadius = new CornerRadius(10);
                chip.Background = new SolidColorBrush(Colors.Transparent);
                chip.BorderThickness = new Thickness(selected ? 3 : 2);
                chip.BorderBrush = new SolidColorBrush(selected
                    ? Color.FromArgb(230, 255, 196, 72)
                    : Color.FromArgb(160, 255, 255, 255));
            }
            else
            {
                chip.CornerRadius = new CornerRadius(6);
                chip.Background = new SolidColorBrush(Colors.Transparent);
                chip.BorderThickness = new Thickness(selected ? 3 : 0);
            }

            if (selected && highlight != "Frame")
            {
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(230, 255, 196, 72));
            }
            else if (!selected && highlight != "Frame")
            {
                chip.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }

        private async Task ApplyDecorToSelectedAsync()
        {
            var visual = GetSlideVisual(_currentPageIndex);
            if (visual?.Page == null || !visual.Page.DecorativeMode || visual.Words == null)
            {
                return;
            }

            string highlight = "None";
            if (WordHighlightCombo?.SelectedItem != null)
            {
                highlight = WordHighlightCombo.SelectedItem.ToString();
            }

            bool applyAll = visual.Page.ApplyToAll;
            bool any = false;
            for (int i = 0; i < visual.Words.Count; i++)
            {
                var decor = visual.Words[i];
                if (!applyAll && !decor.Selected)
                {
                    continue;
                }

                any = true;
                decor.HasSaved = true;
                decor.Weight = _settings.FontWeight;
                decor.Color = _settings.TextColor;
                decor.Highlight = highlight;
            }

            if (!any)
            {
                return;
            }

            ApplyWordCharacterFormats(visual.Editor, visual.Page, visual.Words);
            visual.Page.WordDecorJson = SerializeWordDecor(visual.Words);
            visual.OverlayRetries = 0;
            visual.Editor?.UpdateLayout();
            RefreshDecorOverlay(visual);
            await _dbService.SavePageAsync(visual.Page);
        }

        private static string[] SplitWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new string[0];
            }

            return NormalizePlainText(text).Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static List<WordDecor> ParseWordDecor(string json)
        {
            var list = new List<WordDecor>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return list;
            }

            try
            {
                var arr = JsonArray.Parse(json);
                foreach (var item in arr)
                {
                    if (item.ValueType != JsonValueType.Object)
                    {
                        continue;
                    }

                    var obj = item.GetObject();
                    list.Add(new WordDecor
                    {
                        Weight = obj.ContainsKey("w") ? obj["w"].GetString() : "SemiBold",
                        Color = obj.ContainsKey("c") ? obj["c"].GetString() : "Black",
                        Highlight = obj.ContainsKey("h") ? obj["h"].GetString() : "None"
                    });
                }
            }
            catch
            {
            }

            return list;
        }

        private static string SerializeWordDecor(List<WordDecor> words)
        {
            var arr = new JsonArray();
            foreach (var word in words)
            {
                var obj = new JsonObject();
                obj["w"] = JsonValue.CreateStringValue(word.Weight ?? "SemiBold");
                obj["c"] = JsonValue.CreateStringValue(word.Color ?? "Black");
                obj["h"] = JsonValue.CreateStringValue(word.Highlight ?? "None");
                arr.Add(obj);
            }

            return arr.Stringify();
        }

        private void UpdatePageIndicator()
        {
            int count = _currentTopic?.Pages.Count ?? 0;
            if (_currentTopic == null)
            {
                PageIndicatorText.Text = "Trang —";
            }
            else
            {
                PageIndicatorText.Text = "Trang " + (_currentPageIndex + 1) + " / " + Math.Max(count, 1);
            }

            if (PrevPageButton != null)
            {
                PrevPageButton.IsEnabled = _currentTopic != null && _currentPageIndex > 0;
            }
            if (NextPageButton != null)
            {
                NextPageButton.IsEnabled = _currentTopic != null;
            }
        }

        private void CarouselScroller_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (_currentTopic == null || _slides.Count == 0)
            {
                return;
            }

            int delta = e.GetCurrentPoint(CarouselScroller).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            e.Handled = true;
            var ignore = GoToPageAsync(_currentPageIndex + (delta < 0 ? 1 : -1), delta < 0);
        }

        private async Task GoToPageAsync(int index, bool createIfMissing)
        {
            if (_currentTopic == null)
            {
                return;
            }

            if (index < 0)
            {
                return;
            }

            if (index >= _currentTopic.Pages.Count)
            {
                if (!createIfMissing)
                {
                    return;
                }

                var newPage = new CanvasPage { TopicId = _currentTopic.Id, PageIndex = _currentTopic.Pages.Count };
                CopyStyleToPage(newPage);
                await _dbService.SavePageAsync(newPage);
                _currentTopic.Pages.Add(newPage);
                CarouselTrack.Children.Add(CreateSlide(newPage));
                RelayoutCarousel();
            }

            if (index == _currentPageIndex)
            {
                ScrollToIndex(index, true);
                return;
            }

            _currentPageIndex = index;
            BindCurrentSlide();
            ScrollToIndex(index, true);
        }

        private async void CanvasEditor_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingEditor || _isUpdatingPager || _isPagingOverflow || _isFitting)
            {
                return;
            }

            var editor = sender as Editor;
            if (editor?.Document == null)
            {
                return;
            }

            editor.Document.GetText(TextGetOptions.NoHidden, out string text);
            text = NormalizePlainText(text).TrimEnd();

            var page = editor.Tag as CanvasPage;
            if (page == null)
            {
                return;
            }

            if (page.Text == text)
            {
                return;
            }

            page.Text = text;
            await MaybeEnsureTrailingEmptyScreenAsync(page);

            var capturedEditor = editor;
            var capturedPage = page;
            ScheduleFitAndMaybeOverflow(capturedEditor, capturedPage);
        }

        private void ScheduleFitAndMaybeOverflow(Editor editor, CanvasPage page)
        {
            if (editor == null || _isFitting || _isUpdatingEditor || _isPagingOverflow)
            {
                return;
            }

            if (!_fitQueued.Add(editor))
            {
                return;
            }

            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                _fitQueued.Remove(editor);
                var ignoredTask = FitAndMaybeOverflowAsync(editor, page);
            });
        }

        private async Task FitAndMaybeOverflowAsync(Editor editor, CanvasPage page)
        {
            if (_isUpdatingEditor || _isUpdatingPager || _isPagingOverflow || _isFitting || editor == null)
            {
                return;
            }

            _isFitting = true;
            _isUpdatingEditor = true;
            try
            {
                EnsureTypedTypeface(editor);
                FitTextToViewport(editor);
            }
            finally
            {
                _isUpdatingEditor = false;
                _isFitting = false;
            }

            if (page != null && page.DecorativeMode)
            {
                await _dbService.SavePageAsync(page);
                return;
            }

            if (EditorTextOverflows(editor) && IsAtMinScale(editor))
            {
                await OverflowUntilFitsAsync(editor, page);
                return;
            }

            if (page != null)
            {
                await _dbService.SavePageAsync(page);
            }
        }

        private static void TrimEditorToText(Editor editor, string keep)
        {
            if (editor?.Document == null)
            {
                return;
            }

            editor.Document.GetText(TextGetOptions.NoHidden, out string live);
            live = live ?? "";
            int cut = Math.Min(keep.Length, live.Length);
            if (cut < 0)
            {
                cut = 0;
            }

            try
            {
                ITextRange tail = editor.Document.GetRange(cut, live.Length);
                tail.SetText(TextSetOptions.None, "");
                editor.Document.Selection.SetRange(cut, cut);
            }
            catch (ArgumentException)
            {
            }
        }

        private SlideVisual FindSlide(Editor editor)
        {
            for (int i = 0; i < _slides.Count; i++)
            {
                if (_slides[i].Editor == editor)
                {
                    return _slides[i];
                }
            }

            return null;
        }

        private async Task OpenOverflowScreenAsync(string overflow)
        {
            if (_currentTopic == null || string.IsNullOrWhiteSpace(overflow))
            {
                return;
            }

            var chunks = new List<string>();
            string remaining = overflow;
            while (TrySplitOverflow(remaining, MaxWordsPerScreen, out string keep, out string rest))
            {
                chunks.Add(keep);
                remaining = rest;
            }

            if (!string.IsNullOrWhiteSpace(remaining))
            {
                chunks.Add(remaining);
            }

            if (chunks.Count == 0)
            {
                return;
            }

            int insertAt = _currentPageIndex + 1;
            int lastIndex = insertAt;
            _isUpdatingPager = true;
            try
            {
                foreach (string chunk in chunks)
                {
                    var newPage = new CanvasPage
                    {
                        TopicId = _currentTopic.Id,
                        PageIndex = insertAt,
                        Text = chunk
                    };
                    CopyStyleToPage(newPage);
                    await _dbService.SavePageAsync(newPage);
                    _currentTopic.Pages.Insert(insertAt, newPage);

                    var host = CreateSlide(newPage);
                    _slides.RemoveAt(_slides.Count - 1);
                    var visual = host.Tag as SlideVisual;
                    _slides.Insert(insertAt, visual);
                    CarouselTrack.Children.Insert(insertAt, host);
                    lastIndex = insertAt;
                    insertAt++;
                }

                for (int i = 0; i < _currentTopic.Pages.Count; i++)
                {
                    if (_currentTopic.Pages[i].PageIndex != i)
                    {
                        _currentTopic.Pages[i].PageIndex = i;
                        await _dbService.SavePageAsync(_currentTopic.Pages[i]);
                    }
                }

                RelayoutCarousel();
                _currentPageIndex = lastIndex;
                BindCurrentSlide();
                ScrollToIndex(lastIndex, true);
                if (await EnsureTrailingEmptyScreenAsync() && CarouselTrack != null)
                {
                    CarouselTrack.Children.Add(CreateSlide(_currentTopic.Pages[_currentTopic.Pages.Count - 1]));
                    RelayoutCarousel();
                }

                RebuildLibrarySidebar();
            }
            finally
            {
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
                {
                    _isUpdatingPager = false;
                });
            }
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            return text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static bool TrySplitOverflow(string text, int maxWords, out string keep, out string overflow)
        {
            keep = text ?? "";
            overflow = "";
            if (CountWords(keep) <= maxWords)
            {
                return false;
            }

            int words = 0;
            int i = 0;
            while (i < keep.Length)
            {
                while (i < keep.Length && char.IsWhiteSpace(keep[i]))
                {
                    i++;
                }

                if (i >= keep.Length)
                {
                    break;
                }

                while (i < keep.Length && !char.IsWhiteSpace(keep[i]))
                {
                    i++;
                }

                words++;
                if (words == maxWords)
                {
                    keep = keep.Substring(0, i).TrimEnd();
                    overflow = i < text.Length ? text.Substring(i).TrimStart() : "";
                    return overflow.Length > 0;
                }
            }

            return false;
        }

        private static void FitTypeToWordCount(int words, float baseSize, float baseLead, out float size, out float leading)
        {
            float t = 0;
            if (words > NormalWordCount)
            {
                t = Math.Min(1f, (words - NormalWordCount) / (float)ExtraWordCount);
            }

            size = baseSize * (1f - (0.25f * t));
            leading = baseLead * (1f - (0.12f * t));
            float floor = MinFitSize(baseSize);
            if (size < floor) size = floor;
            if (leading < 1.0f) leading = 1.0f;
        }

        private async Task OverflowUntilFitsAsync(Editor editor, CanvasPage page)
        {
            if (editor?.Document == null || page == null)
            {
                return;
            }

            bool previousFit = _isFitting;
            _isFitting = true;
            string overflow = "";
            string text = "";
            try
            {
                var pageStyle = StyleFromPage(page);
                float minSize = MinFitSize(ReadBaseSize(pageStyle));
                float minLead = ScaleLead(minSize, ReadBaseSize(pageStyle), ReadBaseLead(pageStyle));
                ApplySizeOnly(editor, minSize, minLead);
                editor.UpdateLayout();

                editor.Document.GetText(TextGetOptions.NoHidden, out text);
                text = NormalizePlainText(text).TrimEnd();
                int guard = 0;
                while (EditorTextOverflows(editor) && guard++ < 64)
                {
                    int words = CountWords(text);
                    if (words <= 1 || !TrySplitOverflow(text, words - 1, out string keep, out string rest))
                    {
                        break;
                    }

                    overflow = string.IsNullOrEmpty(overflow) ? rest : rest + " " + overflow;
                    text = keep;
                    _isUpdatingEditor = true;
                    try
                    {
                        page.Text = keep;
                        TrimEditorToText(editor, keep);
                        ApplySizeOnly(editor, minSize, minLead);
                        editor.UpdateLayout();
                    }
                    finally
                    {
                        _isUpdatingEditor = false;
                    }
                }

                overflow = overflow.Trim();
            }
            finally
            {
                _isFitting = previousFit;
            }

            if (overflow.Length == 0)
            {
                return;
            }

            var slide = FindSlide(editor);
            if (slide != null)
            {
                slide.FittedWordCount = CountWords(text);
            }

            await _dbService.SavePageAsync(page);
            _isPagingOverflow = true;
            try
            {
                await OpenOverflowScreenAsync(overflow);
            }
            finally
            {
                _isPagingOverflow = false;
            }

            var next = GetSlideVisual(_currentPageIndex);
            if (next?.Editor != null && next.Page != null && next.Page != page)
            {
                await FitAndMaybeOverflowAsync(next.Editor, next.Page);
            }
        }

        private void LockEditorTypeface(Editor editor, CanvasSettings settings)
        {
            if (editor == null || settings == null)
            {
                return;
            }

            var face = CanvasTypefaces.ToXaml(settings.FontFamily, settings.FontWeight);
            editor.LockTypeface(
                CanvasTypefaces.FamilyName(settings.FontFamily),
                WeightValue(settings.FontWeight),
                face);
            try
            {
                editor.FontFamily = face;
                editor.FontWeight = settings.FontWeight == "Bold" ? FontWeights.Bold
                                  : settings.FontWeight == "SemiBold" ? FontWeights.SemiBold
                                  : FontWeights.Normal;
            }
            catch (ArgumentException)
            {
            }
        }

        private void EnsureTypedTypeface(Editor editor)
        {
            if (editor?.Document == null || string.IsNullOrEmpty(editor.LockedFontName))
            {
                return;
            }

            try
            {
                var range = editor.Document.GetRange(0, 0);
                range.Expand(TextRangeUnit.Story);
                bool nameOk = string.Equals(range.CharacterFormat.Name, editor.LockedFontName, StringComparison.OrdinalIgnoreCase);
                bool weightOk = editor.LockedWeight <= 0 || range.CharacterFormat.Weight == editor.LockedWeight;
                if (nameOk && weightOk)
                {
                    return;
                }

                bool previous = _isUpdatingEditor;
                _isUpdatingEditor = true;
                editor.BeginUpdate();
                try
                {
                    range.CharacterFormat.Name = editor.LockedFontName;
                    if (editor.LockedWeight > 0)
                    {
                        range.CharacterFormat.Weight = editor.LockedWeight;
                    }

                    if (editor.LockedFontFamily != null)
                    {
                        editor.FontFamily = editor.LockedFontFamily;
                    }

                    editor.LockTypeface(editor.LockedFontName, editor.LockedWeight, editor.LockedFontFamily);
                }
                finally
                {
                    editor.EndUpdate();
                    _isUpdatingEditor = previous;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private void ScheduleFitToViewport(Editor editor)
        {
            if (editor == null || _isFitting || _isUpdatingEditor || _isPagingOverflow)
            {
                return;
            }

            if (!_fitQueued.Add(editor))
            {
                return;
            }

            var captured = editor;
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                _fitQueued.Remove(captured);
                if (_isFitting || _isUpdatingEditor || _isPagingOverflow)
                {
                    return;
                }

                _isFitting = true;
                _isUpdatingEditor = true;
                try
                {
                    EnsureTypedTypeface(captured);
                    FitTextToViewport(captured);
                }
                finally
                {
                    _isUpdatingEditor = false;
                    _isFitting = false;
                }
            });
        }

        private static float ReadBaseSize(CanvasSettings settings)
        {
            float size = 96f;
            if (settings != null &&
                float.TryParse(settings.FontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                size = parsed;
            }

            return size;
        }

        private static float ReadBaseLead(CanvasSettings settings)
        {
            float leading = 1.1f;
            if (settings != null &&
                !string.IsNullOrWhiteSpace(settings.LineSpacing) &&
                float.TryParse(settings.LineSpacing, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
                parsed > 0.5f)
            {
                leading = parsed;
            }

            return leading;
        }

        private static float MinFitSize(float baseSize)
        {
            float floor = baseSize * MinScaleFactor;
            return floor < 12f ? 12f : floor;
        }

        private static float ScaleLead(float size, float maxSize, float baseLead)
        {
            float minSize = MinFitSize(maxSize);
            if (maxSize <= minSize)
            {
                return baseLead < 1f ? 1f : baseLead;
            }

            float t = (size - minSize) / (maxSize - minSize);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float lead = baseLead * (0.88f + (0.12f * t));
            return lead < 1f ? 1f : lead;
        }

        private static float GetCurrentFontSize(Editor editor, float fallback)
        {
            try
            {
                var range = editor.Document.GetRange(0, 0);
                range.Expand(TextRangeUnit.Story);
                float size = range.CharacterFormat.Size;
                return size > 8f ? size : fallback;
            }
            catch (ArgumentException)
            {
                return fallback;
            }
        }

        private void ApplySizeOnly(Editor editor, float size, float leading)
        {
            if (editor?.Document == null)
            {
                return;
            }

            bool previous = _isUpdatingEditor;
            _isUpdatingEditor = true;
            editor.BeginUpdate();
            try
            {
                editor.Document.GetText(TextGetOptions.None, out string raw);
                int end = string.IsNullOrEmpty(raw) ? 0 : raw.Length;
                ITextRange range = end > 0
                    ? editor.Document.GetRange(0, end)
                    : editor.Document.GetRange(0, 0);
                if (end <= 0)
                {
                    range.Expand(TextRangeUnit.Story);
                }

                range.CharacterFormat.Size = size;
                editor.FontSize = size;
                if (!string.IsNullOrEmpty(editor.LockedFontName))
                {
                    range.CharacterFormat.Name = editor.LockedFontName;
                    if (editor.LockedWeight > 0)
                    {
                        range.CharacterFormat.Weight = editor.LockedWeight;
                    }
                }

                range.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, leading);
                var defaults = editor.Document.GetDefaultCharacterFormat();
                defaults.Size = size;
                editor.Document.SetDefaultCharacterFormat(defaults);
                var paragraphDefaults = editor.Document.GetDefaultParagraphFormat();
                paragraphDefaults.SetLineSpacing(LineSpacingRule.Multiple, leading);
                editor.Document.SetDefaultParagraphFormat(paragraphDefaults);
                if (editor.Document.Selection != null)
                {
                    editor.Document.Selection.CharacterFormat.Size = size;
                }
            }
            catch (ArgumentException)
            {
            }
            finally
            {
                editor.EndUpdate();
                _isUpdatingEditor = previous;
            }
        }

        private void FitTextToViewport(Editor editor)
        {
            if (editor?.Document == null || editor.ActualHeight < 8 || editor.ActualWidth < 8)
            {
                return;
            }

            bool previousUpdate = _isUpdatingEditor;
            _isUpdatingEditor = true;
            try
            {
                FitTextToViewportCore(editor);
            }
            finally
            {
                _isUpdatingEditor = previousUpdate;
            }
        }

        private void FitTextToViewportCore(Editor editor)
        {
            if (editor?.Document == null || editor.ActualHeight < 8 || editor.ActualWidth < 8)
            {
                return;
            }

            var page = editor.Tag as CanvasPage;
            var settings = page != null ? StyleFromPage(page) : _settings;
            float maxSize = ReadBaseSize(settings);
            float minSize = MinFitSize(maxSize);
            float baseLead = ReadBaseLead(settings);
            editor.Document.GetText(TextGetOptions.None, out string raw);
            if (IsDocumentVisuallyEmpty(raw))
            {
                ApplySizeOnly(editor, maxSize, baseLead);
                return;
            }

            ApplySizeOnly(editor, maxSize, baseLead);
            editor.UpdateLayout();
            if (!EditorTextOverflows(editor))
            {
                return;
            }

            ApplySizeOnly(editor, minSize, ScaleLead(minSize, maxSize, baseLead));
            editor.UpdateLayout();
            if (EditorTextOverflows(editor))
            {
                return;
            }

            float lo = minSize;
            float hi = maxSize;
            for (int i = 0; i < 12; i++)
            {
                float mid = (lo + hi) * 0.5f;
                ApplySizeOnly(editor, mid, ScaleLead(mid, maxSize, baseLead));
                editor.UpdateLayout();
                if (EditorTextOverflows(editor))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }

            ApplySizeOnly(editor, lo, ScaleLead(lo, maxSize, baseLead));
        }

        private bool IsAtMinScale(Editor editor)
        {
            var page = editor?.Tag as CanvasPage;
            var settings = page != null ? StyleFromPage(page) : _settings;
            float maxSize = ReadBaseSize(settings);
            float minSize = MinFitSize(maxSize);
            return GetCurrentFontSize(editor, maxSize) <= minSize + 0.8f;
        }

        private static Windows.UI.Text.FontWeight ProbeWeight(int weight)
        {
            if (weight >= 700)
            {
                return FontWeights.Bold;
            }

            if (weight >= 600)
            {
                return FontWeights.SemiBold;
            }

            return FontWeights.Normal;
        }

        private bool EditorTextOverflows(Editor editor)
        {
            if (editor?.Document == null || editor.ActualHeight < 8 || editor.ActualWidth < 8)
            {
                return false;
            }

            editor.Document.GetText(TextGetOptions.NoHidden, out string raw);
            string text = NormalizePlainText(raw).TrimEnd();
            if (IsDocumentVisuallyEmpty(text))
            {
                return false;
            }

            var page = editor.Tag as CanvasPage;
            var settings = page != null ? StyleFromPage(page) : _settings;
            float maxSize = ReadBaseSize(settings);
            float size = GetCurrentFontSize(editor, maxSize);
            float leading = ScaleLead(size, maxSize, ReadBaseLead(settings));
            var probe = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontFamily = editor.LockedFontFamily ?? editor.FontFamily,
                FontWeight = ProbeWeight(editor.LockedWeight),
                TextWrapping = TextWrapping.Wrap,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Max(1, size * leading),
                Width = editor.ActualWidth
            };
            probe.Measure(new Size(editor.ActualWidth, double.PositiveInfinity));
            if (probe.DesiredSize.Height > editor.ActualHeight + 1 ||
                probe.DesiredSize.Width > editor.ActualWidth + 1)
            {
                return true;
            }

            return LastGlyphClipped(editor);
        }

        private static bool LastGlyphClipped(Editor editor)
        {
            editor.Document.GetText(TextGetOptions.None, out string raw);
            if (IsDocumentVisuallyEmpty(raw))
            {
                return false;
            }

            double limitH = editor.ActualHeight;
            double limitW = editor.ActualWidth;
            try
            {
                int end = raw.Length;
                while (end > 0 && (raw[end - 1] == '\r' || raw[end - 1] == '\n'))
                {
                    end--;
                }

                if (end <= 0)
                {
                    return false;
                }

                var tail = editor.Document.GetRange(Math.Max(0, end - 1), end);
                tail.GetPoint(
                    HorizontalCharacterAlignment.Right,
                    VerticalCharacterAlignment.Bottom,
                    PointOptions.ClientCoordinates,
                    out Point bottom);
                tail.GetPoint(
                    HorizontalCharacterAlignment.Left,
                    VerticalCharacterAlignment.Top,
                    PointOptions.ClientCoordinates,
                    out Point top);
                const double edge = 2;
                return top.Y < -edge || bottom.Y > limitH - edge || bottom.X > limitW - edge;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsDocumentVisuallyEmpty(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return true;
            }

            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c != '\r' && c != '\n' && !char.IsWhiteSpace(c))
                {
                    return false;
                }
            }

            return true;
        }

        private async void Formatting_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings || _isUpdatingEditor || _isFitting)
            {
                return;
            }

            ReadSettingsFromUi();
            bool colorOnly = sender == TextColorCombo || sender == BackgroundCombo;
            if (sender == TextColorCombo)
            {
                HarmonizePaperForChosenInk();
            }

            if (GetSlideVisual(_currentPageIndex)?.Page != null &&
                GetSlideVisual(_currentPageIndex).Page.DecorativeMode)
            {
                var ignore = ApplyDecorToSelectedAsync();
            }
            else if (colorOnly)
            {
                ApplyInkOnly();
            }
            else
            {
                bool applyTypeface = sender == FontFamilyCombo || sender == FontWeightCombo;
                ApplyFormatting(applyTypeface);
            }

            ApplyCanvasBackground();
            await PersistSettingsAsync();
        }

        private async void RoundedRectToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings)
            {
                return;
            }

            ApplyRoundedRect();
            await PersistSettingsAsync();
        }

        private void ApplyRoundedRect()
        {
            if (_currentTextContainer == null || RoundedRectToggle == null) return;

            if (RoundedRectToggle.IsOn)
            {
                _currentTextContainer.CornerRadius = new CornerRadius(32);
                _currentTextContainer.Background = new SolidColorBrush(Colors.White) { Opacity = 0.14 };
                _currentTextContainer.Padding = new Thickness(16);
            }
            else
            {
                _currentTextContainer.CornerRadius = new CornerRadius(0);
                _currentTextContainer.Background = new SolidColorBrush(Colors.Transparent);
                _currentTextContainer.Padding = new Thickness(0);
            }
        }

        private void ApplyFormatting(bool applyTypeface = true)
        {
            bool applyToAll = true;
            var visual = GetSlideVisual(_currentPageIndex);
            if (visual?.Page != null)
            {
                applyToAll = visual.Page.ApplyToAll;
            }

            ApplyFormattingToEditor(_currentEditor, _settings, applyToAll, applyTypeface);
            if (applyTypeface && applyToAll)
            {
                LockEditorTypeface(_currentEditor, _settings);
            }

            ScheduleFitToViewport(_currentEditor);
        }

        private void HarmonizePaperForChosenInk()
        {
            if (_settings == null)
            {
                return;
            }

            var papers = new List<string>();
            for (int i = 0; i < PaperSwatches.Length; i++)
            {
                papers.Add(PaperSwatches[i][1]);
            }

            string paper = CanvasContrast.PaperForInk(
                ResolveColor(_settings.TextColor),
                _settings.BackgroundColor,
                papers);
            if (string.Equals(paper, _settings.BackgroundColor, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _settings.BackgroundColor = paper;
            bool previous = _isLoadingSettings;
            _isLoadingSettings = true;
            SelectBackgroundByHex(paper);
            _isLoadingSettings = previous;
        }

        private void ApplyInkOnly()
        {
            var editor = _currentEditor;
            if (editor?.Document == null || _settings == null)
            {
                return;
            }

            bool applyToAll = GetSlideVisual(_currentPageIndex)?.Page != null &&
                              GetSlideVisual(_currentPageIndex).Page.ApplyToAll;
            Color ink = ResolveColor(_settings.TextColor);
            editor.BeginUpdate();
            try
            {
                ITextRange range;
                if (applyToAll)
                {
                    range = editor.Document.GetRange(0, 0);
                    range.Expand(TextRangeUnit.Story);
                }
                else
                {
                    range = editor.Document.Selection;
                }

                range.CharacterFormat.ForegroundColor = ink;
                editor.Foreground = new SolidColorBrush(ink);
                editor.ApplySelectionTint(ink, ParseHex(_settings.BackgroundColor));
                var defaults = editor.Document.GetDefaultCharacterFormat();
                defaults.ForegroundColor = ink;
                editor.Document.SetDefaultCharacterFormat(defaults);
            }
            catch (ArgumentException)
            {
            }
            finally
            {
                editor.EndUpdate();
            }
        }

        private void ApplyFormattingToEditor(Editor editor, CanvasSettings settings, bool applyToAll = true, bool applyTypeface = true)
        {
            if (editor == null || editor.Document == null || settings == null) return;

            editor.BeginUpdate();
            try
            {
                ApplyFormattingToEditorCore(editor, settings, applyToAll, applyTypeface);
            }
            catch (ArgumentException)
            {
            }
            finally
            {
                editor.EndUpdate();
            }
        }

        private void ApplyFormattingToEditorCore(Editor editor, CanvasSettings settings, bool applyToAll, bool applyTypeface)
        {
            int caret = editor.Document.Selection.EndPosition;
            int start = editor.Document.Selection.StartPosition;
            ITextRange range;
            if (applyToAll)
            {
                range = editor.Document.GetRange(0, 0);
                range.Expand(TextRangeUnit.Story);
            }
            else
            {
                range = editor.Document.Selection;
            }

            editor.Document.GetText(TextGetOptions.NoHidden, out string liveText);
            int words = CountWords(NormalizePlainText(liveText));

            float size = 96f;
            if (float.TryParse(settings.FontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSize))
            {
                size = parsedSize;
            }

            float leading = 1.1f;
            if (!string.IsNullOrWhiteSpace(settings.LineSpacing) &&
                float.TryParse(settings.LineSpacing, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedLeading) &&
                parsedLeading > 0.5f)
            {
                leading = parsedLeading;
            }

            if (applyToAll)
            {
                FitTypeToWordCount(words, size, leading, out size, out leading);
            }
            range.CharacterFormat.Size = size;
            string ink = settings.TextColor;

            if (applyTypeface && !string.IsNullOrWhiteSpace(settings.FontFamily))
            {
                if (applyToAll)
                {
                    try
                    {
                        editor.FontFamily = CanvasTypefaces.ToXaml(settings.FontFamily, settings.FontWeight);
                    }
                    catch (ArgumentException)
                    {
                        editor.FontFamily = new FontFamily("Segoe UI");
                    }
                }

                try
                {
                    range.CharacterFormat.Name = CanvasTypefaces.FamilyName(settings.FontFamily);
                }
                catch (ArgumentException)
                {
                    range.CharacterFormat.Name = "Segoe UI";
                }
            }

            string weight = settings.FontWeight;
            range.CharacterFormat.Weight = weight == "Bold" ? FontWeights.Bold.Weight
                                         : weight == "SemiBold" ? FontWeights.SemiBold.Weight
                                         : FontWeights.Normal.Weight;

            string align = settings.Alignment;
            range.ParagraphFormat.Alignment = align == "Right" ? ParagraphAlignment.Right
                                            : align == "Center" ? ParagraphAlignment.Center
                                            : align == "Justify" ? ParagraphAlignment.Justify
                                            : ParagraphAlignment.Left;

            range.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, leading);
            range.ParagraphFormat.SpaceAfter = 0;
            range.ParagraphFormat.SpaceBefore = 0;

            range.CharacterFormat.ForegroundColor = ResolveColor(ink);
            range.CharacterFormat.BackgroundColor = Colors.Transparent;
            editor.Foreground = new SolidColorBrush(ResolveColor(ink));
            editor.ApplySelectionTint(ResolveColor(ink), ParseHex(settings.BackgroundColor));

            var defaults = editor.Document.GetDefaultCharacterFormat();
            defaults.Size = range.CharacterFormat.Size;
            if (applyTypeface)
            {
                defaults.Name = range.CharacterFormat.Name;
            }

            defaults.Weight = range.CharacterFormat.Weight;
            defaults.ForegroundColor = range.CharacterFormat.ForegroundColor;
            defaults.BackgroundColor = Colors.Transparent;
            editor.Document.SetDefaultCharacterFormat(defaults);

            var paragraphDefaults = editor.Document.GetDefaultParagraphFormat();
            paragraphDefaults.Alignment = range.ParagraphFormat.Alignment;
            paragraphDefaults.SetLineSpacing(LineSpacingRule.Multiple, leading);
            paragraphDefaults.SpaceAfter = 0;
            paragraphDefaults.SpaceBefore = 0;
            editor.Document.SetDefaultParagraphFormat(paragraphDefaults);

            if (applyToAll)
            {
                editor.Document.Selection.SetRange(caret, caret);
            }
            else
            {
                editor.Document.Selection.SetRange(start, caret);
            }

            if (applyTypeface && applyToAll)
            {
                LockEditorTypeface(editor, settings);
            }
            else if (applyTypeface)
            {
                editor.Document.Selection.CharacterFormat.Name = CanvasTypefaces.FamilyName(settings.FontFamily);
                editor.Document.Selection.CharacterFormat.Weight = WeightValue(settings.FontWeight);
            }
        }

        private static class CanvasTypefaces
        {
            public static string FamilyName(string name)
            {
                if (name == "Cormorant Infant" || name == "Fraunces" || name == "Literata")
                {
                    return name;
                }

                return "Be Vietnam Pro";
            }

            public static FontFamily ToXaml(string name, string weight)
            {
                string family = FamilyName(name);
                bool bold = weight == "Bold";
                bool semi = weight == "SemiBold";
                switch (family)
                {
                    case "Cormorant Infant":
                        return new FontFamily("ms-appx:///Assets/Fonts/" + (bold ? "CormorantInfant-Bold.ttf" : semi ? "CormorantInfant-SemiBold.ttf" : "CormorantInfant-Regular.ttf") + "#Cormorant Infant");
                    case "Fraunces":
                        return new FontFamily("ms-appx:///Assets/Fonts/Fraunces.ttf#Fraunces");
                    case "Literata":
                        return new FontFamily("ms-appx:///Assets/Fonts/Literata.ttf#Literata");
                    default:
                        return new FontFamily("ms-appx:///Assets/Fonts/" + (bold ? "BeVietnamPro-Bold.ttf" : semi ? "BeVietnamPro-SemiBold.ttf" : "BeVietnamPro-Regular.ttf") + "#Be Vietnam Pro");
                }
            }
        }

        private void ApplySlideChrome(Border root, Border textContainer, CanvasSettings settings)
        {
            if (root != null)
            {
                root.Background = new SolidColorBrush(ParseHex(settings.BackgroundColor));
            }

            if (textContainer == null)
            {
                return;
            }

            if (settings.UseRoundedRect)
            {
                textContainer.CornerRadius = new CornerRadius(32);
                textContainer.Background = new SolidColorBrush(Colors.White) { Opacity = 0.14 };
                textContainer.Padding = new Thickness(16);
            }
            else
            {
                textContainer.CornerRadius = new CornerRadius(0);
                textContainer.Background = new SolidColorBrush(Colors.Transparent);
                textContainer.Padding = new Thickness(0);
            }

            var visual = root?.Tag as SlideVisual;
            if (visual != null)
            {
                PaintSlideMeta(visual, settings.BackgroundColor);
            }
        }

        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTopic == null || _currentTopic.Pages.Count == 0)
            {
                await ShowMessageAsync("Nothing to export", "Open a topic with at least one page first.");
                return;
            }

            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add("*");

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
            {
                return;
            }

            ReadSettingsFromUi();
            ExportButton.IsEnabled = false;
            ExportOverlay.Visibility = Visibility.Visible;
            ExportProgressBar.Minimum = 0;
            ExportProgressBar.Maximum = _currentTopic.Pages.Count;
            ExportProgressBar.Value = 0;
            ExportStatusText.Text = "Exporting 0 of " + _currentTopic.Pages.Count + " photos...";

            try
            {
                await ExportPagesAsPhotosAsync(folder, _currentTopic.Title, _currentTopic.Pages, _settings);
                await ShowMessageAsync(
                    "Export complete",
                    "Saved " + _currentTopic.Pages.Count + " high-resolution photos to:\n" + folder.Path);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("Export failed", ex.Message);
            }
            finally
            {
                ExportOverlay.Visibility = Visibility.Collapsed;
                ExportButton.IsEnabled = true;
            }
        }

        private async Task ExportPagesAsPhotosAsync(
            StorageFolder folder,
            string topicTitle,
            IList<CanvasPage> pages,
            CanvasSettings settings)
        {
            string prefix = SanitizeFileName(topicTitle);

            for (int i = 0; i < pages.Count; i++)
            {
                ExportProgressBar.Value = i + 1;
                ExportStatusText.Text = "Exporting " + (i + 1) + " of " + pages.Count + " photos...";

                var surface = BuildExportSurface(pages[i]);
                ExportHost.Children.Add(surface);
                surface.Measure(new Size(1080, 1920));
                surface.Arrange(new Rect(0, 0, 1080, 1920));
                surface.UpdateLayout();
                LayoutExportDecor(surface);
                await Task.Delay(40);

                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(surface, 2160, 3840);

                var file = await folder.CreateFileAsync(
                    prefix + "_" + (i + 1).ToString("000") + ".png",
                    CreationCollisionOption.ReplaceExisting);

                var pixels = await bitmap.GetPixelsAsync();
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        (uint)bitmap.PixelWidth,
                        (uint)bitmap.PixelHeight,
                        192,
                        192,
                        pixels.ToArray());
                    await encoder.FlushAsync();
                }

                ExportHost.Children.Remove(surface);
            }
        }

        private FrameworkElement BuildExportSurface(CanvasPage page)
        {
            var settings = StyleFromPage(page);
            if (!double.TryParse(settings.FontSize, NumberStyles.Float, CultureInfo.InvariantCulture, out double fontSize))
            {
                fontSize = 96;
            }

            double leading = 1.1;
            if (!string.IsNullOrWhiteSpace(settings.LineSpacing))
            {
                double.TryParse(settings.LineSpacing, NumberStyles.Float, CultureInfo.InvariantCulture, out leading);
                if (leading < 0.8) leading = 1.1;
            }

            FitTypeToWordCount(CountWords(page.Text), (float)fontSize, (float)leading, out float fittedSize, out float fittedLead);
            fontSize = fittedSize;
            leading = fittedLead;

            FrameworkElement body;
            if (page.DecorativeMode)
            {
                body = BuildExportDecorLayer(page, settings, fontSize);
            }
            else
            {
                body = new TextBlock
                {
                    Text = NormalizePlainText(page.Text),
                    FontFamily = CanvasTypefaces.ToXaml(settings.FontFamily, settings.FontWeight),
                    FontSize = fontSize,
                    FontWeight = settings.FontWeight == "Bold" ? FontWeights.Bold
                                : settings.FontWeight == "SemiBold" ? FontWeights.SemiBold
                                : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = fontSize * leading,
                    LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                    TextAlignment = settings.Alignment == "Right" ? TextAlignment.Right
                                  : settings.Alignment == "Center" ? TextAlignment.Center
                                  : settings.Alignment == "Justify" ? TextAlignment.Justify
                                  : TextAlignment.Left,
                    Foreground = new SolidColorBrush(ResolveColor(settings.TextColor)),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8)
                };
            }

            var textContainer = new Border
            {
                Child = body,
                Margin = new Thickness(1080 * 0.07, 1920 * 0.075, 1080 * 0.07, 1920 * 0.075),
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            if (settings.UseRoundedRect)
            {
                textContainer.CornerRadius = new CornerRadius(32);
                textContainer.Padding = new Thickness(16);
                Color inner = ParseHex(settings.BackgroundColor);
                inner.A = 40;
                textContainer.Background = new SolidColorBrush(Colors.White) { Opacity = 0.12 };
            }

            var stage = new Grid();
            stage.Children.Add(textContainer);
            int pageNumber = 1;
            int pageCount = 1;
            if (_currentTopic?.Pages != null)
            {
                pageCount = Math.Max(1, _currentTopic.Pages.Count);
                int found = _currentTopic.Pages.IndexOf(page);
                if (found >= 0)
                {
                    pageNumber = found + 1;
                }
            }

            var pageVisual = new SlideVisual { Page = page };
            var badge = BuildPageIndexBlob(pageVisual);
            if (pageVisual.PageNumberText != null)
            {
                pageVisual.PageNumberText.Text = pageNumber.ToString("00", CultureInfo.InvariantCulture);
            }

            if (pageVisual.PageTotalText != null)
            {
                pageVisual.PageTotalText.Text = "/ " + pageCount.ToString(CultureInfo.InvariantCulture);
            }

            stage.Children.Add(badge);
            stage.Children.Add(BuildCanvasFooterBlob(
                pageVisual,
                CanvasTitleText(),
                AlbumNameText(),
                settings.BackgroundColor,
                false));

            return new Border
            {
                Width = 1080,
                Height = 1920,
                Background = new SolidColorBrush(ParseHex(settings.BackgroundColor)),
                CornerRadius = new CornerRadius(28),
                Child = stage
            };
        }

        private FrameworkElement BuildExportDecorLayer(CanvasPage page, CanvasSettings settings, double fontSize)
        {
            var editor = new Editor
            {
                Style = Resources["CanvasEditorStyle"] as Style,
                FontFamily = CanvasTypefaces.ToXaml(settings.FontFamily, settings.FontWeight),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = new SolidColorBrush(ResolveColor(settings.TextColor)),
                Padding = new Thickness(0),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                IsSpellCheckEnabled = false,
                UseSystemFocusVisuals = false,
                IsReadOnly = true,
                IsHitTestVisible = false,
                IsTabStop = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            editor.ApplySelectionTint(
                ResolveColor(CanvasContrast.InkFor(settings.BackgroundColor, settings.TextColor)),
                ParseHex(settings.BackgroundColor));
            ScrollViewer.SetVerticalScrollMode(editor, ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(editor, ScrollBarVisibility.Disabled);
            ScrollViewer.SetHorizontalScrollMode(editor, ScrollMode.Disabled);

            var highlights = new Canvas { IsHitTestVisible = false };
            var layer = new Grid();
            layer.Children.Add(highlights);
            layer.Children.Add(editor);

            bool previous = _isUpdatingEditor;
            _isUpdatingEditor = true;
            LockEditorTypeface(editor, settings);
            editor.Document.SetText(TextSetOptions.None, NormalizeRichText(page.Text));
            ApplyFormattingToEditor(editor, settings);
            LockEditorTypeface(editor, settings);
            var words = BuildWordModel(editor, page);
            ApplyWordCharacterFormats(editor, page, words);
            _isUpdatingEditor = previous;

            layer.Tag = new OverlayPack
            {
                Editor = editor,
                Highlights = highlights,
                Words = words
            };
            return layer;
        }

        private void LayoutExportDecor(FrameworkElement surface)
        {
            var pack = FindOverlayPack(surface);
            if (pack?.Editor == null || pack.Highlights == null)
            {
                return;
            }

            pack.Editor.UpdateLayout();
            LayoutDecorOverlay(pack.Editor, pack.Highlights, null, pack.Words, false, null);
        }

        private static OverlayPack FindOverlayPack(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is FrameworkElement element && element.Tag is OverlayPack pack)
            {
                return pack;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindOverlayPack(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Brush GetBrush(string key, Color fallback)
        {
            return new SolidColorBrush(fallback);
        }

        private static Color ResolveColor(string color)
        {
            if (color == "White") return Colors.White;
            if (color == "Kem") return ParseHex("#F6E7C1");
            if (color == "Ivory") return ParseHex("#FFF8E7");
            if (color == "Gold") return ParseHex("#E6C36A");
            if (color == "Sand") return ParseHex("#D4B896");
            if (color == "Navy") return ParseHex("#1B365D");
            if (color == "Charcoal") return ParseHex("#2B2B2B");
            if (color == "Brown") return ParseHex("#5C4033");
            if (color == "Maroon") return ParseHex("#7A1F2B");
            if (color == "Olive") return ParseHex("#4A5D23");
            if (color == "Forest") return ParseHex("#1F4D3A");
            if (color == "Teal") return ParseHex("#0F6B6B");
            if (color == "Red") return Colors.Red;
            if (color == "Crimson") return ParseHex("#C41E3A");
            if (color == "Coral") return ParseHex("#E36A5D");
            if (color == "Orange") return ParseHex("#E07A2F");
            if (color == "Amber") return ParseHex("#D4A017");
            if (color == "Blue") return Colors.Blue;
            if (color == "Sky") return ParseHex("#4A90C8");
            if (color == "Indigo") return ParseHex("#3D348B");
            if (color == "Violet") return ParseHex("#7C5CBF");
            if (color == "Pink") return ParseHex("#D97BA0");
            if (color == "Sage") return ParseHex("#8FA882");
            if (color == "Mint") return ParseHex("#7BC4A8");
            if (color == "Dark Gray") return Colors.DarkGray;
            return Colors.Black;
        }

        private void ApplyCanvasBackground()
        {
            if (_currentRoot == null) return;
            Color color = ParseHex(_settings.BackgroundColor);
            _currentRoot.Background = new SolidColorBrush(color);
            var visual = GetSlideVisual(_currentPageIndex);
            if (visual != null)
            {
                PaintSlideMeta(visual, _settings.BackgroundColor);
            }
        }

        private async Task SaveCurrentPageStyleAsync()
        {
            if (_currentTopic == null || _currentTopic.Pages.Count == 0)
            {
                return;
            }

            var page = _currentTopic.Pages[_currentPageIndex];
            CopyStyleToPage(page);
            await _dbService.SavePageAsync(page);
        }

        private void ApplyStyleFromPage(CanvasPage page)
        {
            if (page == null)
            {
                return;
            }

            bool previous = _isLoadingSettings;
            _isLoadingSettings = true;
            _settings = StyleFromPage(page);
            SelectComboItem(FontFamilyCombo, _settings.FontFamily);
            SelectComboItem(FontSizeCombo, _settings.FontSize);
            SelectComboItem(LineSpacingCombo, _settings.LineSpacing);
            SelectComboItem(FontWeightCombo, _settings.FontWeight);
            SelectComboItem(AlignmentCombo, _settings.Alignment);
            SelectComboItem(TextColorCombo, _settings.TextColor);
            SelectBackgroundByHex(_settings.BackgroundColor);
            if (RoundedRectToggle != null)
            {
                RoundedRectToggle.IsOn = _settings.UseRoundedRect;
            }
            _isLoadingSettings = previous;
        }

        private void CopyStyleToPage(CanvasPage page)
        {
            if (page == null || _settings == null)
            {
                return;
            }

            page.FontFamily = _settings.FontFamily;
            page.FontSize = _settings.FontSize;
            page.LineSpacing = _settings.LineSpacing;
            page.FontWeight = _settings.FontWeight;
            page.Alignment = _settings.Alignment;
            page.TextColor = _settings.TextColor;
            page.BackgroundColor = _settings.BackgroundColor;
            page.UseRoundedRect = _settings.UseRoundedRect;
            page.ApplyToAll = false;
        }

        private static CanvasSettings StyleFromPage(CanvasPage page)
        {
            return new CanvasSettings
            {
                FontFamily = CanvasTypefaces.FamilyName(page.FontFamily),
                FontSize = string.IsNullOrWhiteSpace(page.FontSize) ? "96" : page.FontSize,
                LineSpacing = string.IsNullOrWhiteSpace(page.LineSpacing) ? "1.1" : page.LineSpacing,
                FontWeight = string.IsNullOrWhiteSpace(page.FontWeight) ? "SemiBold" : page.FontWeight,
                Alignment = string.IsNullOrWhiteSpace(page.Alignment) ? "Center" : page.Alignment,
                TextColor = string.IsNullOrWhiteSpace(page.TextColor) ? "Black" : page.TextColor,
                BackgroundColor = string.IsNullOrWhiteSpace(page.BackgroundColor) ? "#FAF4EC" : page.BackgroundColor,
                UseRoundedRect = page.UseRoundedRect
            };
        }

        private void SelectBackgroundByHex(string hex)
        {
            if (BackgroundCombo == null)
            {
                return;
            }

            string name = BackgroundNameFromHex(hex);
            SelectComboItem(BackgroundCombo, name);
        }

        private static string BackgroundHexFromName(string name)
        {
            for (int i = 0; i < PaperSwatches.Length; i++)
            {
                if (string.Equals(PaperSwatches[i][0], name, StringComparison.OrdinalIgnoreCase))
                {
                    return PaperSwatches[i][1];
                }
            }

            return "#FAF4EC";
        }

        private static string BackgroundNameFromHex(string hex)
        {
            if (string.Equals(hex, "#111111", StringComparison.OrdinalIgnoreCase))
            {
                return "Mực";
            }

            if (string.Equals(hex, "#2F2F2F", StringComparison.OrdinalIgnoreCase))
            {
                return "Xám";
            }

            for (int i = 0; i < PaperSwatches.Length; i++)
            {
                if (string.Equals(PaperSwatches[i][1], hex, StringComparison.OrdinalIgnoreCase))
                {
                    return PaperSwatches[i][0];
                }
            }

            return "Giấy";
        }

        private static Color ParseHex(string hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex))
                {
                    return Colors.White;
                }

                hex = hex.Trim().TrimStart('#');
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return Color.FromArgb(255, r, g, b);
                }
            }
            catch
            {
            }

            return Colors.White;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Topic";
            }

            foreach (char invalid in System.IO.Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid, '_');
            }

            name = name.Trim();
            return string.IsNullOrWhiteSpace(name) ? "Topic" : name;
        }

        private const string SettingsFileName = "canvas-defaults.json";

        private static async Task<CanvasSettings> LoadCanvasSettingsAsync()
        {
            try
            {
                var file = await ApplicationData.Current.LocalFolder.TryGetItemAsync(SettingsFileName) as StorageFile;
                if (file == null)
                {
                    return new CanvasSettings();
                }

                string json = await FileIO.ReadTextAsync(file);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new CanvasSettings();
                }

                var obj = JsonObject.Parse(json);
                return new CanvasSettings
                {
                    FontFamily = GetJsonString(obj, "FontFamily", "Be Vietnam Pro"),
                    FontSize = GetJsonString(obj, "FontSize", "96"),
                    LineSpacing = GetJsonString(obj, "LineSpacing", "1.1"),
                    FontWeight = GetJsonString(obj, "FontWeight", "SemiBold"),
                    Alignment = GetJsonString(obj, "Alignment", "Center"),
                    TextColor = GetJsonString(obj, "TextColor", "Black"),
                    BackgroundColor = GetJsonString(obj, "BackgroundColor", "#FAF4EC"),
                    UseRoundedRect = obj.ContainsKey("UseRoundedRect") && obj["UseRoundedRect"].ValueType == JsonValueType.Boolean && obj["UseRoundedRect"].GetBoolean()
                };
            }
            catch
            {
                return new CanvasSettings();
            }
        }

        private static async Task SaveCanvasSettingsAsync(CanvasSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            var obj = new JsonObject();
            obj["FontFamily"] = JsonValue.CreateStringValue(settings.FontFamily ?? "Be Vietnam Pro");
            obj["FontSize"] = JsonValue.CreateStringValue(settings.FontSize ?? "96");
            obj["LineSpacing"] = JsonValue.CreateStringValue(settings.LineSpacing ?? "1.1");
            obj["FontWeight"] = JsonValue.CreateStringValue(settings.FontWeight ?? "SemiBold");
            obj["Alignment"] = JsonValue.CreateStringValue(settings.Alignment ?? "Center");
            obj["TextColor"] = JsonValue.CreateStringValue(settings.TextColor ?? "Black");
            obj["BackgroundColor"] = JsonValue.CreateStringValue(settings.BackgroundColor ?? "#FAF4EC");
            obj["UseRoundedRect"] = JsonValue.CreateBooleanValue(settings.UseRoundedRect);

            var file = await ApplicationData.Current.LocalFolder.CreateFileAsync(
                SettingsFileName,
                CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, obj.Stringify());
        }

        private static string NormalizeRichText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Replace("\r\n", "\r").Replace("\n", "\r");
        }

        private static string NormalizePlainText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            return text.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string GetJsonString(JsonObject obj, string key, string fallback)
        {
            if (obj.ContainsKey(key) && obj[key].ValueType == JsonValueType.String)
            {
                var value = obj[key].GetString();
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }

            return fallback;
        }

        private static async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            };
            var root = Window.Current?.Content as UIElement;
            if (root != null)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            await dialog.ShowAsync();
        }
    }
}
