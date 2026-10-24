using System;
using System.Threading.Tasks;
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

        public static Task<bool> UnlockAsync(XamlRoot root, PrivacyStore store)
        {
            return ShowAsync(root, store, store.HasPassword ? Mode.Unlock : Mode.Create);
        }

        public static Task<bool> ChangeAsync(XamlRoot root, PrivacyStore store)
        {
            return ShowAsync(root, store, store.HasPassword ? Mode.Change : Mode.Create);
        }

        public static Task<bool> ResetAsync(XamlRoot root, PrivacyStore store)
        {
            return ShowAsync(root, store, Mode.Reset);
        }

        static async Task<bool> ShowAsync(XamlRoot root, PrivacyStore store, Mode mode)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (store == null) throw new ArgumentNullException(nameof(store));
            var current = CreatePasswordBox();
            var next = CreatePasswordBox();
            var confirm = CreatePasswordBox();
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
            var panel = new StackPanel { Spacing = 12, MinWidth = 240, MaxWidth = 320 };
            string help = mode == Mode.Unlock ? "验证后显示当前请求查看的金额。" :
                mode == Mode.Reset ? "重置会清除原密码并将薪资归零，无法恢复。" :
                mode == Mode.Change ? "输入原密码后修改，已有薪资会保留。" :
                "设置后，隐藏金额无需密码，重新显示时需要验证。";
            panel.Children.Add(new TextBlock
            {
                Text = help,
                TextWrapping = TextWrapping.Wrap,
                Foreground = mode == Mode.Reset ? new SolidColorBrush(Colors.OrangeRed) : null
            });
            if (mode == Mode.Unlock || mode == Mode.Change)
                AddPassword(panel, mode == Mode.Unlock ? "金额密码" : "原密码", current);
            if (mode != Mode.Unlock)
            {
                AddPassword(panel, "新密码 · 6–128 位", next);
                AddPassword(panel, "再次输入新密码", confirm);
            }
            if (mode == Mode.Reset) panel.Children.Add(consent);
            panel.Children.Add(error);
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
                Title = mode == Mode.Unlock ? "验证密码" : mode == Mode.Create ? "设置金额密码" :
                    mode == Mode.Change ? "修改金额密码" : "忘记密码 · 重置",
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = mode == Mode.Unlock ? "验证并显示" : mode == Mode.Reset ? "确认归零并设置新密码" : "保存密码",
                CloseButtonText = "取消",
                SecondaryButtonText = mode == Mode.Unlock || mode == Mode.Change ? "忘记密码" : string.Empty,
                DefaultButton = ContentDialogButton.Primary
            };
            bool succeeded = false;
            dialog.Opened += delegate
            {
                (mode == Mode.Unlock || mode == Mode.Change ? current : next).Focus(FocusState.Programmatic);
            };
            dialog.PrimaryButtonClick += delegate(ContentDialog sender, ContentDialogButtonClickEventArgs args)
            {
                try
                {
                    if (mode == Mode.Unlock)
                    {
                        if (!store.Verify(current.Password)) throw new ArgumentException("密码不正确，请重新输入。");
                    }
                    else
                    {
                        PrivacyStore.ValidatePassword(next.Password);
                        if (next.Password != confirm.Password) throw new ArgumentException("两次输入的新密码不一致。");
                        if (mode == Mode.Reset && consent.IsChecked != true)
                            throw new ArgumentException("请先确认清除原密码并将薪资归零。");
                        if (mode == Mode.Create) store.SetInitialPassword(next.Password);
                        else if (mode == Mode.Change) store.ChangePassword(current.Password, next.Password);
                        else store.ResetPasswordAndSalary(next.Password);
                    }
                    succeeded = true;
                }
                catch (Exception ex)
                {
                    args.Cancel = true;
                    error.Text = ex.Message;
                    error.Visibility = Visibility.Visible;
                    current.Password = string.Empty;
                    (mode == Mode.Unlock || mode == Mode.Change ? current : next).Focus(FocusState.Programmatic);
                }
            };
            ContentDialogResult result;
            try { result = await dialog.ShowAsync(); }
            finally
            {
                current.Password = string.Empty;
                next.Password = string.Empty;
                confirm.Password = string.Empty;
            }
            // Await closure before opening another ContentDialog on the same XamlRoot.
            if (result == ContentDialogResult.Secondary)
            {
                await ResetAsync(root, store);
                return false;
            }
            return result == ContentDialogResult.Primary && succeeded;
        }

        static PasswordBox CreatePasswordBox()
        {
            var input = new PasswordBox
            {
                MaxLength = 128,
                FontSize = 14,
                MinHeight = 40,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(10, 8, 10, 8)
            };
            input.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
            var light = new ResourceDictionary();
            light["TextControlBorderBrushFocused"] = new SolidColorBrush(ColorHelper.FromArgb(255, 138, 138, 142));
            var dark = new ResourceDictionary();
            dark["TextControlBorderBrushFocused"] = new SolidColorBrush(ColorHelper.FromArgb(255, 152, 152, 157));
            input.Resources.ThemeDictionaries["Light"] = light;
            input.Resources.ThemeDictionaries["Dark"] = dark;
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
            input.Header = label;
            AutomationProperties.SetName(input, label);
            panel.Children.Add(input);
        }
    }
}
