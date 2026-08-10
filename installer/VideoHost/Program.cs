using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace VIP1132.InstallerVisual
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length < 2 || !File.Exists(args[0])) return;

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new DeploymentWindow(args[0], args[1]));
        }
    }

    internal sealed class DeploymentWindow : Window
    {
        private readonly string _videoPath;
        private readonly string _sentinelPath;
        private readonly MediaElement _video;
        private readonly DispatcherTimer _sentinelTimer;

        public DeploymentWindow(string videoPath, string sentinelPath)
        {
            _videoPath = videoPath;
            _sentinelPath = sentinelPath;

            Title = "VIP 1132 Installation";
            Width = 590;
            Height = 680;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = true;
            Background = new SolidColorBrush(Color.FromRgb(2, 3, 8));

            var outer = new Grid { Margin = new Thickness(1) };
            outer.Background = new LinearGradientBrush(
                Color.FromRgb(82, 246, 255),
                Color.FromRgb(255, 60, 203),
                45);
            outer.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(168, 85, 247),
                BlurRadius = 38,
                ShadowDepth = 0,
                Opacity = 0.7
            };

            var panel = new Grid
            {
                Margin = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(4, 6, 12))
            };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var top = new Grid { Margin = new Thickness(25, 19, 18, 10) };
            top.ColumnDefinitions.Add(new ColumnDefinition());
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(new TextBlock
            {
                Text = "VIP 1132  /  SYSTEM DEPLOYMENT",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 172, 201)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var close = new Button
            {
                Content = "×",
                Width = 31,
                Height = 31,
                FontSize = 20,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Hide installation animation"
            };
            close.Click += delegate { Close(); };
            Grid.SetColumn(close, 1);
            top.Children.Add(close);
            Grid.SetRow(top, 0);
            panel.Children.Add(top);

            var mediaFrame = new Border
            {
                Margin = new Thickness(44, 8, 44, 8),
                Background = Brushes.Black,
                BorderBrush = new SolidColorBrush(Color.FromArgb(115, 82, 246, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(22),
                ClipToBounds = true,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(82, 246, 255),
                    BlurRadius = 24,
                    ShadowDepth = 0,
                    Opacity = 0.35
                }
            };
            _video = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = Stretch.UniformToFill,
                IsMuted = true
            };
            _video.MediaEnded += delegate
            {
                _video.Position = TimeSpan.Zero;
                _video.Play();
            };
            mediaFrame.Child = _video;
            Grid.SetRow(mediaFrame, 1);
            panel.Children.Add(mediaFrame);

            var status = new StackPanel { Margin = new Thickness(44, 15, 44, 30) };
            status.Children.Add(new TextBlock
            {
                Text = "INSTALLING VIP 1132",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            status.Children.Add(new TextBlock
            {
                Text = "Preparing the deployment console and Zoom automation engine…",
                Foreground = new SolidColorBrush(Color.FromRgb(143, 160, 184)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 17)
            });
            var progress = new ProgressBar
            {
                Height = 7,
                IsIndeterminate = true,
                Background = new SolidColorBrush(Color.FromRgb(18, 24, 36)),
                Foreground = new SolidColorBrush(Color.FromRgb(82, 246, 255)),
                BorderThickness = new Thickness(0)
            };
            status.Children.Add(progress);
            status.Children.Add(new TextBlock
            {
                Text = "The installer will finish automatically.",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 117, 143)),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });
            Grid.SetRow(status, 2);
            panel.Children.Add(status);

            outer.Children.Add(panel);
            Content = outer;

            Loaded += OnLoaded;
            Closed += delegate { _sentinelTimer.Stop(); _video.Stop(); };
            _sentinelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _sentinelTimer.Tick += delegate
            {
                if (File.Exists(_sentinelPath)) Close();
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _video.Source = new Uri(_videoPath, UriKind.Absolute);
            _video.Play();
            _sentinelTimer.Start();
        }
    }
}
