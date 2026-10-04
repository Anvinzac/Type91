using SQLite;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace RectifyPad.Classes
{
    [Table("CanvasPages")]
    public class CanvasPage : INotifyPropertyChanged
    {
        private int _id;
        private int _topicId;
        private string _text = "";
        private int _pageIndex;
        private string _fontFamily = "Be Vietnam Pro";
        private string _fontSize = "96";
        private string _fontWeight = "SemiBold";
        private string _alignment = "Center";
        private string _textColor = "Graphite";
        private string _backgroundColor = "#F5F5F7";
        private bool _useRoundedRect;
        private string _lineSpacing = "1.1";
        private bool _applyToAll;
        private bool _decorativeMode;
        private string _wordDecorJson = "";

        [PrimaryKey, AutoIncrement]
        public int Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        [Indexed]
        public int TopicId
        {
            get => _topicId;
            set
            {
                if (_topicId != value)
                {
                    _topicId = value;
                    OnPropertyChanged();
                }
            }
        }

        public int PageIndex
        {
            get => _pageIndex;
            set
            {
                if (_pageIndex != value)
                {
                    _pageIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FontFamily
        {
            get => _fontFamily;
            set { if (_fontFamily != value) { _fontFamily = value; OnPropertyChanged(); } }
        }

        public string FontSize
        {
            get => _fontSize;
            set { if (_fontSize != value) { _fontSize = value; OnPropertyChanged(); } }
        }

        public string FontWeight
        {
            get => _fontWeight;
            set { if (_fontWeight != value) { _fontWeight = value; OnPropertyChanged(); } }
        }

        public string Alignment
        {
            get => _alignment;
            set { if (_alignment != value) { _alignment = value; OnPropertyChanged(); } }
        }

        public string TextColor
        {
            get => _textColor;
            set { if (_textColor != value) { _textColor = value; OnPropertyChanged(); } }
        }

        public string BackgroundColor
        {
            get => _backgroundColor;
            set { if (_backgroundColor != value) { _backgroundColor = value; OnPropertyChanged(); } }
        }

        public bool UseRoundedRect
        {
            get => _useRoundedRect;
            set { if (_useRoundedRect != value) { _useRoundedRect = value; OnPropertyChanged(); } }
        }

        public string LineSpacing
        {
            get => _lineSpacing;
            set { if (_lineSpacing != value) { _lineSpacing = value; OnPropertyChanged(); } }
        }

        public bool ApplyToAll
        {
            get => _applyToAll;
            set { if (_applyToAll != value) { _applyToAll = value; OnPropertyChanged(); } }
        }

        public bool DecorativeMode
        {
            get => _decorativeMode;
            set { if (_decorativeMode != value) { _decorativeMode = value; OnPropertyChanged(); } }
        }

        public string WordDecorJson
        {
            get => _wordDecorJson;
            set { if (_wordDecorJson != value) { _wordDecorJson = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class CanvasTheme
    {
        public CanvasTheme(string name, string paperName, string paperHex, string ink, bool isDark)
        {
            Name = name;
            PaperName = paperName;
            PaperHex = paperHex;
            Ink = ink;
            IsDark = isDark;
        }

        public string Name { get; }
        public string PaperName { get; }
        public string PaperHex { get; }
        public string Ink { get; }
        public bool IsDark { get; }
    }

    public static class CanvasThemes
    {
        public static readonly string[][] Inks =
        {
            new[] { "Graphite", "#1D1D1F" },
            new[] { "Slate", "#48484A" },
            new[] { "Ink Blue", "#1E2A3B" },
            new[] { "Silver", "#F5F5F7" },
            new[] { "Mist", "#D1D1D6" },
            new[] { "Ice", "#DCE6F2" }
        };

        public static readonly CanvasTheme[] All =
        {
            new CanvasTheme("Pearl", "Ngọc trai", "#FBFBFD", "Graphite", false),
            new CanvasTheme("Mist", "Sương", "#F5F5F7", "Graphite", false),
            new CanvasTheme("Glacier", "Sương lam", "#EEF2F7", "Ink Blue", false),
            new CanvasTheme("Stone", "Đá", "#EFEDEA", "Slate", false),
            new CanvasTheme("Platinum", "Bạch kim", "#E8E8ED", "Graphite", false),
            new CanvasTheme("Titanium", "Titan", "#3A3A3C", "Silver", true),
            new CanvasTheme("Space Gray", "Xám không gian", "#2C2C2E", "Mist", true),
            new CanvasTheme("Midnight Blue", "Lam đêm", "#0E1726", "Ice", true),
            new CanvasTheme("Graphite", "Than chì", "#1D1D1F", "Silver", true),
            new CanvasTheme("Obsidian", "Đêm sâu", "#000000", "Silver", true)
        };

        public static CanvasTheme Find(string name)
        {
            foreach (var theme in All)
            {
                if (string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return theme;
                }
            }

            return null;
        }

        public static CanvasTheme Match(string paperHex, string ink)
        {
            foreach (var theme in All)
            {
                if (string.Equals(theme.PaperHex, paperHex, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(theme.Ink, ink, StringComparison.OrdinalIgnoreCase))
                {
                    return theme;
                }
            }

            return null;
        }

        public static bool TryInkHex(string name, out string hex)
        {
            foreach (var ink in Inks)
            {
                if (string.Equals(ink[0], name, StringComparison.OrdinalIgnoreCase))
                {
                    hex = ink[1];
                    return true;
                }
            }

            hex = null;
            return false;
        }
    }

    public static class CanvasContrast
    {
        public static bool IsDarkBackground(string hex)
        {
            int rgb = ParseRgb(hex);
            int r = (rgb >> 16) & 255;
            int g = (rgb >> 8) & 255;
            int b = rgb & 255;
            return (r * 0.299 + g * 0.587 + b * 0.114) < 140;
        }

        public static bool IsDarkInk(string name)
        {
            return name == "Black" || name == "Graphite" || name == "Slate" || name == "Ink Blue" ||
                   name == "Navy" || name == "Dark Gray" || name == "Blue";
        }

        public static string InkFor(string backgroundHex, string textColor)
        {
            return string.IsNullOrWhiteSpace(textColor) ? "Black" : textColor;
        }

        public static string PaperForInk(Color ink, string currentHex, IReadOnlyList<string> papers)
        {
            string current = string.IsNullOrWhiteSpace(currentHex) ? "#F5F5F7" : currentHex;
            if (current[0] != '#')
            {
                current = "#" + current;
            }

            Color paperNow = ColorFromRgb(ParseRgb(current));
            if (ContrastRatio(ink, paperNow) >= 4.5)
            {
                return current;
            }

            bool inkIsLight = RelativeLuminance(ink) >= 0.55;
            string best = current;
            double bestScore = -1;
            if (papers != null)
            {
                for (int i = 0; i < papers.Count; i++)
                {
                    string hex = papers[i];
                    Color paper = ColorFromRgb(ParseRgb(hex));
                    double contrast = ContrastRatio(ink, paper);
                    if (contrast < 4.5)
                    {
                        continue;
                    }

                    double score = contrast;
                    bool paperIsDark = RelativeLuminance(paper) < 0.35;
                    if (inkIsLight == paperIsDark)
                    {
                        score += 20;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = hex;
                    }
                }
            }

            if (bestScore >= 0)
            {
                return best;
            }

            bestScore = ContrastRatio(ink, paperNow);
            if (papers != null)
            {
                for (int i = 0; i < papers.Count; i++)
                {
                    string hex = papers[i];
                    double contrast = ContrastRatio(ink, ColorFromRgb(ParseRgb(hex)));
                    if (contrast > bestScore)
                    {
                        bestScore = contrast;
                        best = hex;
                    }
                }
            }

            return best;
        }

        public static Color ColorFromRgb(int rgb)
        {
            return Color.FromArgb(255, (byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255));
        }

        public static Color SelectionFill(Color ink, Color canvas)
        {
            if (RelativeLuminance(ink) >= 0.45)
            {
                return Color.FromArgb(220, 92, 58, 8);
            }

            return Color.FromArgb(210, 255, 214, 82);
        }

        public static Color SelectionInk(Color ink, Color canvas)
        {
            Color wash = Composite(SelectionFill(ink, canvas), canvas);
            if (ContrastRatio(ink, wash) >= 4.5)
            {
                return ink;
            }

            return BestInkOn(wash);
        }

        public static Color InkOnHighlight(Color ink, Color canvas, string highlight)
        {
            if (highlight != "Marker" && highlight != "Pill")
            {
                return ink;
            }

            Color wash = highlight == "Pill"
                ? Color.FromArgb(80, 255, 214, 102)
                : Color.FromArgb(110, 255, 224, 90);
            Color fill = Composite(wash, canvas);
            if (ContrastRatio(ink, fill) >= 4.5)
            {
                return ink;
            }

            return BestInkOn(fill);
        }

        public static Color BestInkOn(Color fill)
        {
            Color warmBlack = Color.FromArgb(255, 28, 20, 8);
            Color cream = Color.FromArgb(255, 246, 231, 193);
            return ContrastRatio(warmBlack, fill) >= ContrastRatio(cream, fill) ? warmBlack : cream;
        }

        public static Color Composite(Color overlay, Color canvas)
        {
            double a = overlay.A / 255.0;
            return Color.FromArgb(
                255,
                Mix(overlay.R, canvas.R, a),
                Mix(overlay.G, canvas.G, a),
                Mix(overlay.B, canvas.B, a));
        }

        public static double ContrastRatio(Color a, Color b)
        {
            double l1 = RelativeLuminance(a);
            double l2 = RelativeLuminance(b);
            if (l1 < l2)
            {
                double swap = l1;
                l1 = l2;
                l2 = swap;
            }

            return (l1 + 0.05) / (l2 + 0.05);
        }

        public static double RelativeLuminance(Color color)
        {
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        private static double Linear(byte channel)
        {
            double s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        private static byte Mix(byte over, byte under, double alpha)
        {
            return (byte)Math.Round(over * alpha + under * (1.0 - alpha));
        }

        private static int ParseRgb(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return 0xFAF4EC;
            }

            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 8)
            {
                hex = hex.Substring(2);
            }

            int value;
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value))
            {
                return value;
            }

            return 0xFAF4EC;
        }
    }
}
