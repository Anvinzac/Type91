using System;
using System.Collections.Generic;
using RectifyPad.Classes;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace RectifyPad
{
    public class Editor : RichEditBox
    {
        private DateTime _focusedAt;
        private Color _inkColor = Colors.Black;
        private Color _canvasColor = Color.FromArgb(255, 250, 244, 236);
        private int _updateLock;
        public string LockedFontName { get; set; }
        public int LockedWeight { get; set; }
        public FontFamily LockedFontFamily { get; set; }

        public Editor()
        {
            Background = new SolidColorBrush(Colors.Transparent);
            BorderBrush = new SolidColorBrush(Colors.Transparent);
            BorderThickness = new Thickness(0);
            Padding = new Thickness(0);
            UseSystemFocusVisuals = false;
            SelectionFlyout = null;
            Loaded += OnEditorLoaded;
            GotFocus += OnEditorGotFocus;
            SelectionChanged += OnSelectionChanged;
            TextChanging += OnTextChanging;
        }

        public void BeginUpdate()
        {
            _updateLock++;
        }

        public void EndUpdate()
        {
            if (_updateLock > 0)
            {
                _updateLock--;
            }
        }

        public void ApplySelectionTint(Color textColor)
        {
            ApplySelectionTint(textColor, _canvasColor);
        }

        public void ApplySelectionTint(Color textColor, Color canvasColor)
        {
            _inkColor = textColor;
            _canvasColor = canvasColor;
            var fill = CanvasContrast.SelectionFill(textColor, canvasColor);
            var tint = new SolidColorBrush(fill);
            SelectionHighlightColor = tint;
            SelectionHighlightColorWhenNotFocused = tint;
        }

        private void OnEditorLoaded(object sender, RoutedEventArgs e)
        {
            ApplyChrome();
            CollapseIfAutoSelectAll();
        }

        private void ApplyChrome()
        {
            Background = new SolidColorBrush(Colors.Transparent);
            BorderBrush = new SolidColorBrush(Colors.Transparent);
            UseSystemFocusVisuals = false;
        }

        protected override void OnGotFocus(RoutedEventArgs e)
        {
            _focusedAt = DateTime.UtcNow;
            ApplyChrome();
            CollapseIfAutoSelectAll();
            base.OnGotFocus(e);
            CollapseIfAutoSelectAll();
            var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Low, CollapseIfAutoSelectAll);
        }

        private void OnSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_updateLock > 0)
            {
                return;
            }

            if ((DateTime.UtcNow - _focusedAt).TotalMilliseconds <= 450 &&
                FocusState != FocusState.Unfocused)
            {
                CollapseIfAutoSelectAll();
            }
        }

        public void LockTypeface(string familyName, int weight, FontFamily fontFamily)
        {
            LockedFontName = familyName;
            LockedWeight = weight;
            LockedFontFamily = fontFamily;
            if (fontFamily != null)
            {
                try
                {
                    FontFamily = fontFamily;
                }
                catch (ArgumentException)
                {
                }
            }

            ApplyLockedTypefaceToSelection();
            ApplyLockedTypefaceToDefaults();
        }

        private void ApplyLockedTypefaceToSelection()
        {
            if (Document?.Selection == null || string.IsNullOrEmpty(LockedFontName))
            {
                return;
            }

            try
            {
                Document.Selection.CharacterFormat.Name = LockedFontName;
                if (LockedWeight > 0)
                {
                    Document.Selection.CharacterFormat.Weight = LockedWeight;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        private void ApplyLockedTypefaceToDefaults()
        {
            if (Document == null || string.IsNullOrEmpty(LockedFontName))
            {
                return;
            }

            try
            {
                var defaults = Document.GetDefaultCharacterFormat();
                defaults.Name = LockedFontName;
                if (LockedWeight > 0)
                {
                    defaults.Weight = LockedWeight;
                }

                Document.SetDefaultCharacterFormat(defaults);
            }
            catch (ArgumentException)
            {
            }
        }

        private void OnEditorGotFocus(object sender, RoutedEventArgs e)
        {
            Document.GetText(Windows.UI.Text.TextGetOptions.None, out string raw);
            if (IsVisuallyEmpty(raw))
            {
                ApplyLockedTypefaceToDefaults();
                ApplyLockedTypefaceToSelection();
            }
        }

        private void OnTextChanging(RichEditBox sender, RichEditBoxTextChangingEventArgs args)
        {
            if (_updateLock > 0)
            {
                return;
            }

            Document.GetText(Windows.UI.Text.TextGetOptions.None, out string raw);
            if (IsVisuallyEmpty(raw))
            {
                ApplyLockedTypefaceToDefaults();
                ApplyLockedTypefaceToSelection();
            }
        }

        private static bool IsVisuallyEmpty(string raw)
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

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right ||
                e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
            {
                var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
                if (ctrl.HasFlag(CoreVirtualKeyStates.Down))
                {
                    return;
                }
            }

            base.OnKeyDown(e);
        }

        private bool IsFullDocumentSelected()
        {
            if (Document?.Selection == null)
            {
                return false;
            }

            Document.GetText(TextGetOptions.None, out string text);
            int length = string.IsNullOrEmpty(text) ? 0 : text.Length;
            int start = Document.Selection.StartPosition;
            int end = Document.Selection.EndPosition;
            return end > start && start <= 0 && end >= Math.Max(0, length - 1);
        }

        private void CollapseIfAutoSelectAll()
        {
            if (IsFullDocumentSelected())
            {
                CollapseSelectionToCaret();
            }
        }

        private void CollapseSelectionToCaret()
        {
            if (Document?.Selection == null)
            {
                return;
            }

            int caret = Document.Selection.EndPosition;
            Document.Selection.SetRange(caret, caret);
        }
    }
}
