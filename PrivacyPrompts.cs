using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OffWorkWidget.WinUI
{
    public static class PrivacyPrompts
    {
        enum Mode { Unlock, Create, Change, Reset }
        static readonly HashSet<XamlRoot> activeRoots = new HashSet<XamlRoot>();

        const int MaxUnlockAttempts = 5;
        public static async Task<bool> UnlockAsync(XamlRoot root, PrivacyStore store, ElementTheme? theme = null)
        {
            if (!store.HasPassword) return await BeginAsync(root, store, Mode.Create, theme);
            // Five failed tries force the reset flow (new password, salary zeroed).
            if (store.UnlockFailures >= MaxUnlockAttempts)
            {
                await BeginAsync(root, store, Mode.Reset, theme);
                return false;
            }
            return await BeginAsync(root, store, Mode.Unlock, theme);
        }

        public static Task<bool> ChangeAsync(XamlRoot root, PrivacyStore store, ElementTheme? theme = null)
        {
            return BeginAsync(root, store, store.HasPassword ? Mode.Change : Mode.Create, theme);
        }

        public static Task<bool> ResetAsync(XamlRoot root, PrivacyStore store, ElementTheme? theme = null)
        {
            return BeginAsync(root, store, Mode.Reset, theme);
        }

        static async Task<bool> BeginAsync(XamlRoot root, PrivacyStore store, Mode mode, ElementTheme? theme)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (!activeRoots.Add(root)) return false;
            try { return await ShowAsync(root, store, mode, theme: theme); }
            finally { activeRoots.Remove(root); }
        }
        static async Task<bool> ShowAsync(XamlRoot root, PrivacyStore store, Mode mode, bool allowHost = true, Window? hostedWindow = null, ElementTheme? theme = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (allowHost && (root.Size.Width < 320 || root.Size.Height < RequiredHeight(mode)))
                return await ShowHostedAsync(root, store, mode, theme);
            var selectedTheme = ResolveTheme(root, theme);
            bool dark = selectedTheme == ElementTheme.Dark;
            var current = CreatePasswordBox(dark);
            var next = CreatePasswordBox(dark);
            var confirm = CreatePasswordBox(dark);
            var consent = new CheckBox
            {
                Content = new TextBlock { Text = "我确认清除原密码，并将薪资归零", TextWrapping = TextWrapping.Wrap }
            };
            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.OrangeRed),
                Visibility = Visibility.Collapsed
            };
            var panel = new StackPanel { Spacing = 10, Width = Math.Min(284, Math.Max(200, root.Size.Width - 40)) };
            string help = mode == Mode.Unlock ? "验证后显示当前请求查看的金额。" :
                mode == Mode.Reset ? "重置会清除原密码并将薪资归零，无法恢复。" :
                mode == Mode.Change ? "输入原密码后修改，已有薪资会保留。" :
                "首次使用：请设置 4–20 位金额密码。隐藏金额无需密码，重新显示时需要验证。";
            var description = new TextBlock { Text = help, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            if (mode == Mode.Reset) description.Foreground = new SolidColorBrush(Colors.OrangeRed);
            panel.Children.Add(description);
            if (mode == Mode.Unlock || mode == Mode.Change)
                AddPassword(panel, mode == Mode.Unlock ? "金额密码" : "原密码", current);
            if (mode != Mode.Unlock)
            {
                AddPassword(panel, "新密码 · 4–20 位", next);
                AddPassword(panel, "再次输入新密码", confirm);
            }
            if (mode == Mode.Reset) panel.Children.Add(consent);
            panel.Children.Add(error);
            bool resetRequested = false;
            bool submitting = false, closed = false;
            using var cancellation = new CancellationTokenSource();
            var form = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                RequestedTheme = selectedTheme,
                Background = SurfaceBrush(dark), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(14),
                Title = mode == Mode.Unlock ? "验证密码" : mode == Mode.Create ? "设置金额密码" :
                    mode == Mode.Change ? "修改金额密码" : "忘记密码 · 重置",
                Content = form,
                PrimaryButtonText = mode == Mode.Unlock ? "验证并显示" : mode == Mode.Reset ? "归零并重设" : "保存密码",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };
            StyleDialog(dialog, dark, hostedWindow != null);
            if (mode == Mode.Unlock || mode == Mode.Change)
            {
                var forgot = new Button
                {
                    Content = "忘记密码？",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Padding = new Thickness(8, 6, 8, 6),
                    BorderThickness = new Thickness(0),
                    Background = FieldBrush(dark)
                };
                forgot.Click += (_, _) => { if (submitting) return; resetRequested = true; dialog.Hide(); };
                panel.Children.Add(forgot);
            }
            WinUI.UiTypography.Apply(dialog, dark);
            bool succeeded = false;
            dialog.Opened += delegate
            {
                (mode == Mode.Unlock || mode == Mode.Change ? current : next).Focus(FocusState.Programmatic);
                if (hostedWindow != null) FitHostedDialog(hostedWindow, root, dialog, panel);
            };
            var hostAppWindow = hostedWindow?.AppWindow;
            Windows.Foundation.TypedEventHandler<ContentDialog, ContentDialogClosingEventArgs> closing = (_, _) => { WinUI.GlassProfile.Event("privacy-closing"); if (!succeeded) cancellation.Cancel(); };
            Windows.Foundation.TypedEventHandler<ContentDialog, ContentDialogClosedEventArgs> dialogClosed = (_, _) => { WinUI.GlassProfile.Event("privacy-closed"); closed = true; cancellation.Cancel(); };
            Windows.Foundation.TypedEventHandler<AppWindow, AppWindowClosingEventArgs> hostClosing = (_, _) => cancellation.Cancel();
            dialog.Closing += closing;
            dialog.Closed += dialogClosed;
            if (hostAppWindow != null) hostAppWindow.Closing += hostClosing;
            dialog.PrimaryButtonClick += async (ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
            {
                if (submitting || cancellation.IsCancellationRequested) { args.Cancel = true; return; }
                var deferral = args.GetDeferral();
                WinUI.GlassProfile.Event("privacy-submit");
                submitting = true;
                dialog.IsPrimaryButtonEnabled = false;
                form.IsEnabled = false;
                try
                {
                    if (mode == Mode.Unlock)
                    {
                        if (!await store.VerifyAsync(current.Password, cancellation.Token))
                        {
                            int failures = store.UnlockFailures;
                            if (failures >= MaxUnlockAttempts)
                            {
                                // Five failures lock the amount and wipe the stored
                                // salary; only a password reset can re-enable it.
                                try { store.SaveSalary(0); } catch (IOException) { }
                                resetRequested = true;
                                dialog.Hide();
                                return;
                            }
                            throw new ArgumentException($"密码不正确，还可尝试 {MaxUnlockAttempts - failures} 次；连续失败 {MaxUnlockAttempts} 次将要求重置密码并清零薪资。");
                        }
                    }
                    else
                    {
                        PrivacyStore.ValidatePassword(next.Password);
                        if (next.Password != confirm.Password) throw new ArgumentException("两次输入的新密码不一致。");
                        if (mode == Mode.Reset && consent.IsChecked != true)
                            throw new ArgumentException("请先确认清除原密码并将薪资归零。");
                        if (mode == Mode.Create) await store.SetInitialPasswordAsync(next.Password, cancellation.Token);
                        else if (mode == Mode.Change) await store.ChangePasswordAsync(current.Password, next.Password, cancellation.Token);
                        else await store.ResetPasswordAndSalaryAsync(next.Password, cancellation.Token);
                    }
                    succeeded = true;
                    WinUI.GlassProfile.Event("privacy-succeeded");
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { WinUI.GlassProfile.Event("privacy-canceled"); }
                catch (Exception ex)
                {
                    WinUI.GlassProfile.Event("privacy-error-" + ex.GetType().Name);
                    if (closed || cancellation.IsCancellationRequested) return;
                    args.Cancel = true;
                    error.Text = ex.Message;
                    error.Visibility = Visibility.Visible;
                    if (hostedWindow != null) FitHostedDialog(hostedWindow, root, dialog, panel);
                    current.Password = string.Empty;
                }
                finally
                {
                    submitting = false;
                    if (!closed && !cancellation.IsCancellationRequested)
                    {
                        dialog.IsPrimaryButtonEnabled = true;
                        form.IsEnabled = true;
                        if (args.Cancel) (mode == Mode.Unlock || mode == Mode.Change ? current : next).Focus(FocusState.Programmatic);
                    }
                    deferral.Complete();
                }
            };
            ContentDialogResult result;
            try { result = await dialog.ShowAsync(); }
            finally
            {
                cancellation.Cancel();
                dialog.Closing -= closing;
                dialog.Closed -= dialogClosed;
                if (hostAppWindow != null) hostAppWindow.Closing -= hostClosing;
                current.Password = string.Empty;
                next.Password = string.Empty;
                confirm.Password = string.Empty;
            }
            // Await closure before opening another ContentDialog on the same XamlRoot.
            if (resetRequested)
            {
                if (hostedWindow != null)
                    PositionHost(hostedWindow, root.ContentIslandEnvironment.AppWindowId, Mode.Reset);
                await ShowAsync(root, store, Mode.Reset, hostedWindow == null, hostedWindow, selectedTheme);
                return false;
            }
            return result == ContentDialogResult.Primary && succeeded;
        }

        static async Task<bool> ShowHostedAsync(XamlRoot source, PrivacyStore store, Mode mode, ElementTheme? requestedTheme)
        {
            var theme = ElementTheme.Light;
            var host = new Window { Title = "薪资隐私" };
            var content = new Grid
            {
                RequestedTheme = theme,
                Background = SurfaceBrush(theme == ElementTheme.Dark)
            };
            content.Resources["ContentDialogSmokeFill"] = new SolidColorBrush(Colors.Transparent);
            host.Content = content;
            var ownerId = source.ContentIslandEnvironment.AppWindowId;
            var owner = AppWindow.GetFromWindowId(ownerId);
            var ownerHandle = Win32Interop.GetWindowFromWindowId(ownerId);
            host.AppWindow.IsShownInSwitchers = false;
            if (host.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = (GetWindowLongPtr(ownerHandle, -20).ToInt64() & 8) != 0;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
            }
            host.ExtendsContentIntoTitleBar = true;
            host.SetTitleBar(null);
            var hostHandle = WinRT.Interop.WindowNative.GetWindowHandle(host);
            long style = (GetWindowLongPtr(hostHandle, -16).ToInt64() & ~0x00CF0000L) | 0x80000000L;
            SetWindowLongPtr(hostHandle, -16, new IntPtr(style));
            SetWindowPos(hostHandle, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
            int corner = 2, noBorder = -2, noFrame = 1;
            DwmSetWindowAttribute(hostHandle, 2, ref noFrame, 4);
            DwmSetWindowAttribute(hostHandle, 33, ref corner, 4);
            DwmSetWindowAttribute(hostHandle, 34, ref noBorder, 4);
            PositionHost(host, ownerId, mode);
            var loaded = new TaskCompletionSource<bool>();
            content.Loaded += (_, _) => loaded.TrySetResult(true);
            bool closed = false;
            Windows.Foundation.TypedEventHandler<AppWindow, object> ownerDestroying = (_, _) =>
            {
                // Keep WinUI window disposal outside the source window destruction callback.
                content.DispatcherQueue.TryEnqueue(() =>
                {
                    if (content.XamlRoot is XamlRoot hostRoot)
                        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(hostRoot))
                            HideDialogs(popup.Child);
                    if (!closed) host.Close();
                });
            };
            owner.Destroying += ownerDestroying;
            host.AppWindow.Closing += (_, _) =>
            {
                if (content.XamlRoot is XamlRoot hostRoot)
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(hostRoot))
                        HideDialogs(popup.Child);
            };
            host.Closed += (_, _) =>
            {
                closed = true;
                loaded.TrySetResult(false);
                if (content.XamlRoot is XamlRoot hostRoot)
                    foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(hostRoot))
                        HideDialogs(popup.Child);
            };
            bool ownerEnabled = IsWindowEnabled(ownerHandle);
            if (ownerEnabled) EnableWindow(ownerHandle, false);
            try
            {
                host.Activate();
                if (!await loaded.Task) return false;
                return await ShowAsync(content.XamlRoot, store, mode, false, host, theme);
            }
            finally
            {
                owner.Destroying -= ownerDestroying;
                if (ownerEnabled) EnableWindow(ownerHandle, true);
                if (!closed) host.Close();
            }
        }

        static void PositionHost(Window host, WindowId ownerId, Mode mode)
        {
            var owner = AppWindow.GetFromWindowId(ownerId);
            double scale = Math.Max(1, GetDpiForWindow(Win32Interop.GetWindowFromWindowId(ownerId)) / 96.0);
            var area = DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Nearest).WorkArea;
            int width = Math.Min(area.Width, (int)Math.Ceiling(324 * scale));
            int height = Math.Min(area.Height, (int)Math.Ceiling((RequiredHeight(mode) + 12) * scale));
            int x = Math.Clamp(owner.Position.X + (owner.Size.Width - width) / 2, area.X, area.X + area.Width - width);
            int y = Math.Clamp(owner.Position.Y + (owner.Size.Height - height) / 2, area.Y, area.Y + area.Height - height);
            host.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }

        static void HideDialogs(DependencyObject element)
        {
            if (element is ContentDialog dialog) { dialog.Hide(); return; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                HideDialogs(VisualTreeHelper.GetChild(element, i));
        }

        static double RequiredHeight(Mode mode) => mode == Mode.Unlock ? 272 : mode == Mode.Create ? 308 : mode == Mode.Change ? 416 : 352;

        [DllImport("user32.dll")]
        static extern bool EnableWindow(IntPtr window, bool enabled);
        [DllImport("user32.dll")]
        static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        static ElementTheme ResolveTheme(XamlRoot root, ElementTheme? requested) => ElementTheme.Light;

        static void FitHostedDialog(Window host, XamlRoot root, ContentDialog dialog, StackPanel panel)
        {
            host.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (panel.XamlRoot == null) return;
                dialog.UpdateLayout();
                var title = System.Linq.Enumerable.FirstOrDefault(Descendants(dialog), e => e is ContentControl c && c.Name == "Title") as FrameworkElement;
                var command = System.Linq.Enumerable.FirstOrDefault(Descendants(dialog), e => e is Grid g && g.Name == "CommandSpace") as FrameworkElement;
                if (title == null || command == null) return;
                panel.Measure(new Windows.Foundation.Size(panel.Width, double.PositiveInfinity));
                double dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(host)) / 96d;
                int inset = Math.Max(0, host.AppWindow.Size.Height - (int)Math.Round(root.Size.Height * dpi));
                int height = (int)Math.Ceiling((panel.DesiredSize.Height + title.ActualHeight + 8 + 32 + command.ActualHeight + 16) * dpi) + inset;
                var area = DisplayArea.GetFromWindowId(host.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
                height = Math.Min(height, area.Height);
                int y = Math.Clamp(host.AppWindow.Position.Y + (host.AppWindow.Size.Height - height) / 2, area.Y, area.Y + area.Height - height);
                host.AppWindow.MoveAndResize(new RectInt32(host.AppWindow.Position.X, y, host.AppWindow.Size.Width, height));
            });
        }
        static SolidColorBrush SurfaceBrush(bool dark) => new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 37, 38, 43) : Colors.White);
        static SolidColorBrush FieldBrush(bool dark) => new SolidColorBrush(dark ? ColorHelper.FromArgb(255, 53, 54, 61) : ColorHelper.FromArgb(255, 230, 231, 237));

        static void StyleDialog(ContentDialog dialog, bool dark, bool hosted)
        {
            var transparent = new SolidColorBrush(Colors.Transparent);
            dialog.Resources["ContentDialogMinWidth"] = 300d;
            dialog.Resources["ContentDialogPadding"] = new Thickness(16);
            dialog.Resources["ContentDialogTitleMargin"] = new Thickness(0, 0, 0, 8);
            dialog.Resources["ContentDialogTopOverlay"] = transparent;
            dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
            dialog.Resources["ContentDialogBorderWidth"] = new Thickness(0);
            dialog.Resources["ContentDialogBorderBrush"] = transparent;
            if (hosted)
            {
                dialog.CornerRadius = new CornerRadius(0);
                dialog.Resources["ContentDialogSmokeFill"] = transparent;
                dialog.Resources["ContentDialogLightDismissOverlayBackground"] = transparent;
            }
            dialog.Resources["AccentButtonBackground"] = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 122, 255));
            dialog.Resources["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 112, 240));
            dialog.Resources["AccentButtonBackgroundPressed"] = new SolidColorBrush(ColorHelper.FromArgb(255, 0, 100, 220));
            foreach (string state in new[] { "", "PointerOver", "Pressed" })
            {
                dialog.Resources["AccentButtonForeground" + state] = new SolidColorBrush(Colors.White);
                dialog.Resources["AccentButtonBorderBrush" + state] = transparent;
                dialog.Resources["ButtonBorderBrush" + state] = transparent;
            }
            var primary = ActionStyle(dark, true);
            dialog.PrimaryButtonStyle = primary;
            dialog.CloseButtonStyle = ActionStyle(dark, false);
            dialog.Opened += (_, _) =>
            {
                foreach (var element in Descendants(dialog))
                {
                    if (element is ContentControl title && title.Name == "Title") title.FontSize = 18;
                    if (hosted && element is Border body && body.Name == "BackgroundElement")
                    {
                        body.Margin = new Thickness(0); body.CornerRadius = new CornerRadius(0);
                        body.HorizontalAlignment = HorizontalAlignment.Stretch; body.VerticalAlignment = VerticalAlignment.Stretch;
                    }
                }
            };
        }

        static Style ActionStyle(bool dark, bool primary)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.MinWidthProperty, 0d));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 36d));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? new SolidColorBrush(ColorHelper.FromArgb(255, 0, 122, 255)) : FieldBrush(dark)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(primary || dark ? Colors.White : ColorHelper.FromArgb(255, 28, 28, 30))));
            return style;
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var item in Descendants(child)) yield return item;
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        static PasswordBox CreatePasswordBox(bool dark)
        {
            var input = new PasswordBox
            {
                MaxLength = 20, FontSize = 13, MinHeight = 38,
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8),
                Background = FieldBrush(dark),
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 7, 10, 7)
            };
            foreach (string state in new[] { "", "PointerOver", "Focused", "Disabled" })
            {
                input.Resources["TextControlBorderBrush" + state] = new SolidColorBrush(Colors.Transparent);
                input.Resources["TextControlBackground" + state] = FieldBrush(dark);
            }
            input.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
            input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);
            input.Loaded += (_, _) => { input.ApplyTemplate(); CenterPasswordContent(input); };
            return input;
        }
        static void CenterPasswordContent(DependencyObject element)
        {
            // The native template does not bind VerticalContentAlignment to its editor.
            if (element is ScrollViewer content && content.Name == "ContentElement")
            {
                content.VerticalAlignment = VerticalAlignment.Center;
                return;
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                CenterPasswordContent(VisualTreeHelper.GetChild(element, i));
        }

        static void AddPassword(StackPanel panel, string label, PasswordBox input)
        {
            AutomationProperties.SetName(input, label);
            var field = new StackPanel { Spacing = 6 };
            field.Children.Add(new TextBlock { Text = label, FontSize = 13 });
            field.Children.Add(WinUI.InputFocusShadow.Wrap(input));
            panel.Children.Add(field);
        }
    }
}
