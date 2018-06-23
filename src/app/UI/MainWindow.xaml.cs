﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GTAPilot
{
    public partial class MainWindow : Window
    {
        class LocalTraceListener : TraceListener
        {
            Action<string> _handler;

            public LocalTraceListener(Action<string> handler) => _handler = handler;

            public override void Write(string message) => WriteLine(message);
            public override void WriteLine(string message) => _handler(message);
        }

        MainWindowViewModel _viewModel;
        DispatcherTimer _fpsTimer = new DispatcherTimer();
        SaveFrameConsumer _captureSink;

        internal MainWindow()
        {
            InitializeComponent();

            Activated += MainWindow_Activated;

            Trace.Listeners.Clear();
            Trace.Listeners.Add(new LocalTraceListener(OnMessage));
        }

        private void OnMessage(string msg)
        {
            Dispatcher.BeginInvoke((Action)(() =>
            {
                bool isAutoScroll = lstLog.Items.Count == 0 || lstLog.Items.Count - 1 == lstLog.SelectedIndex;

                lstLog.Items.Add(msg);

                if (isAutoScroll)
                {
                    lstLog.SelectedIndex = lstLog.Items.Count - 1;

                    if (VisualTreeHelper.GetChildrenCount(lstLog) > 0)
                    {
                        Border border = (Border)VisualTreeHelper.GetChild(lstLog, 0);
                        ScrollViewer scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);
                        scrollViewer.ScrollToBottom();
                    }
                }
            }));
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            Activated -= MainWindow_Activated;

            Topmost = true;
            Topmost = false;

            var dlg = new SourceSelectionDialog();
            dlg.Owner = this;
            dlg.ShowDialog();

            FridaController fridaController = null;
            SystemManager mgr;
            if (dlg.Result == SourceType.Live)
            {
                fridaController = new FridaController((uint)Process.GetProcessesByName("xboxapp")[0].Id, GetScriptContent());
                mgr = new SystemManager(new DesktopFrameProducer(int.Parse(dlg.txtScreenId.Text)), dlg.txtFlightPlan.Text, fridaController);
            }
            else if (dlg.Result == SourceType.Capture)
            {
                _captureSink = new SaveFrameConsumer(dlg.txtCaptureLocation.Text);
                fridaController = new FridaController((uint)Process.GetProcessesByName("xboxapp")[0].Id, GetScriptContent());
                mgr = new SystemManager(new DesktopFrameProducer(int.Parse(dlg.txtScreenId.Text)), _captureSink.HandleFrameArrived, fridaController);
            }
            else if (dlg.Result == SourceType.Playback)
            {
                mgr = new SystemManager(new ReplayFrameProducer(dlg.txtRecording.Text));
            }
            else
            {
                Environment.Exit(0);
                return;
            }

            _viewModel = new MainWindowViewModel(mgr, _captureSink, fridaController);
            DataContext = _viewModel;

            _fpsTimer.Interval = TimeSpan.FromMilliseconds(App.FPS);
            _fpsTimer.Tick += FpsTimer_Tick;
            _fpsTimer.Start();

            if (dlg.Result == SourceType.Capture)
            {
                var progress = new CaptureProgress();

                progress.Owner = this;
                progress.ShowDialog();

                mgr.StopCapture();
            }
        }

        private string GetScriptContent()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GTAPilot.XboxApp.js"))
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private void FpsTimer_Tick(object sender, EventArgs e)
        {
            _viewModel.Tick();
        }

        private void SaveFrameSet_Click(object sender, RoutedEventArgs e)
        {
            var indicator = (IndicatorViewModel)((FrameworkElement)sender).DataContext;

            var frames = string.Join("\r\n", indicator.BadFrames.Select(f => f.ToString()));

            var fn = Path.GetTempFileName() + ".txt";
            File.WriteAllText(fn, frames);

            Process.Start(fn);
        }

        private void Border_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var viewModel = (IndicatorViewModel)((FrameworkElement)sender).DataContext;

            var window = new IndicatorWindow(viewModel);
            window.Owner = this;
            window.ShowDialog();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var t = new Thread(() =>
            {
                var m = new MapWindow();
                m.Show();
                System.Windows.Threading.Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        private void TextBox_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var txt = (TextBox)sender;
            bool isPID = (string)txt.Tag == "PID";

            double next = 0;
            if (isPID)
            {
                var vl = double.Parse(txt.Text);

                if (Keyboard.IsKeyDown(Key.LeftCtrl))
                {
                    next = (vl + Math.Sign(e.Delta) * 0.01);
                }
                else
                {
                    next = (vl + Math.Sign(e.Delta) * 0.1);
                }
            }
            else
            {
                var iTxt = int.Parse(txt.Text);
                bool isHDG = (string)txt.Tag == "HDG";
                bool isALT = (string)txt.Tag == "ALT";

                var v = isALT ? 100 : 1;

                next = (iTxt + Math.Sign(e.Delta) * v);

                if (isALT)
                {
                    next = next / 100;
                    next = Math.Round(next);
                    next = next * 100;
                }

                if (isHDG)
                {
                    if (next > 360) next = 0;
                    if (next < 1) next = 360;
                }

                
            }
            txt.Text = next.ToString();
        }
    }
}
