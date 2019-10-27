﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SharpDX.Direct3D;
using SharpDX.Direct3D9;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.WPF;
using Microsoft.Win32;
using System.Collections.Concurrent;

namespace Kiva_MIDI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>E:\Programming\Personal\2019\Kiva\Kiva-MIDI\MainWindow.xaml

    public partial class MainWindow : Window
    {

        #region Chrome Window scary code
        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case 0x0024:
                    WmGetMinMaxInfo(hwnd, lParam);
                    handled = true;
                    break;
            }
            return (IntPtr)0;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
            int MONITOR_DEFAULTTONEAREST = 0x00000002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO monitorInfo = new MONITORINFO();
                GetMonitorInfo(monitor, monitorInfo);
                RECT rcWorkArea = monitorInfo.rcWork;
                RECT rcMonitorArea = monitorInfo.rcMonitor;
                if (_fullscreen)
                    rcWorkArea = rcMonitorArea;
                mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            /// <summary>x coordinate of point.</summary>
            public int x;
            /// <summary>y coordinate of point.</summary>
            public int y;
            /// <summary>Construct a point of coordinates (x,y).</summary>
            public POINT(int x, int y)
            {
                this.x = x;
                this.y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class MONITORINFO
        {
            public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            public RECT rcMonitor = new RECT();
            public RECT rcWork = new RECT();
            public int dwFlags = 0;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        public struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
            public static readonly RECT Empty = new RECT();
            public int Width { get { return Math.Abs(right - left); } }
            public int Height { get { return bottom - top; } }
            public RECT(int left, int top, int right, int bottom)
            {
                this.left = left;
                this.top = top;
                this.right = right;
                this.bottom = bottom;
            }
            public RECT(RECT rcSrc)
            {
                left = rcSrc.left;
                top = rcSrc.top;
                right = rcSrc.right;
                bottom = rcSrc.bottom;
            }
            public bool IsEmpty { get { return left >= right || top >= bottom; } }
            public override string ToString()
            {
                if (this == Empty) { return "RECT {Empty}"; }
                return "RECT { left : " + left + " / top : " + top + " / right : " + right + " / bottom : " + bottom + " }";
            }
            public override bool Equals(object obj)
            {
                if (!(obj is Rect)) { return false; }
                return (this == (RECT)obj);
            }
            /// <summary>Return the HashCode for this struct (not garanteed to be unique)</summary>
            public override int GetHashCode() => left.GetHashCode() + top.GetHashCode() + right.GetHashCode() + bottom.GetHashCode();
            /// <summary> Determine if 2 RECT are equal (deep compare)</summary>
            public static bool operator ==(RECT rect1, RECT rect2) { return (rect1.left == rect2.left && rect1.top == rect2.top && rect1.right == rect2.right && rect1.bottom == rect2.bottom); }
            /// <summary> Determine if 2 RECT are different(deep compare)</summary>
            public static bool operator !=(RECT rect1, RECT rect2) { return !(rect1 == rect2); }
        }

        [DllImport("user32")]
        internal static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

        [DllImport("User32")]
        internal static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);
        #endregion

        #region Fullscreen        
        static System.Windows.WindowState _cacheWindowState;
        static bool _fullscreen = false;
        static bool Fullscreen
        {
            get => _fullscreen;
        }
        void SetFullscreen(bool value)
        {
            _fullscreen = value;
            if (value)
            {
                _cacheWindowState = WindowState;
                WindowState = System.Windows.WindowState.Normal;
                WindowState = System.Windows.WindowState.Maximized;
                ChromeVisibility = Visibility.Collapsed;
            }
            else
            {
                WindowState = System.Windows.WindowState.Normal;
                WindowState = _cacheWindowState;
                ChromeVisibility = Visibility.Visible;
            }
        }
        #endregion


        public Visibility ChromeVisibility
        {
            get { return (Visibility)GetValue(ChromeVisibilityProperty); }
            set { SetValue(ChromeVisibilityProperty, value); }
        }

        public static readonly DependencyProperty ChromeVisibilityProperty =
            DependencyProperty.Register("ChromeVisibility", typeof(Visibility), typeof(MainWindow), new PropertyMetadata(Visibility.Visible));

        Scene scene;
        D3D11 d3d;
        MIDIPlayer player;

        MIDIFile loadedFle;

        Settings settings = new Settings();

        SettingsWindow settingsWindow = null;
        LoadingMidiForm loadingForm = null;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (s, e) =>
            {
                IntPtr handle = (new WindowInteropHelper(this)).Handle;
                HwndSource.FromHwnd(handle).AddHook(new HwndSourceHook(WindowProc));
            };

            FPS = new FPS();
            Time = new PlayingState();
            Time.PauseChanged += PauseChanged;
            PauseChanged();
            d3d = new D3D11();
            scene = new Scene(settings) { Renderer = d3d, FPS = FPS };
            dx11img.Renderer = scene;
            dx11img.MouseDown += (s, e) => Focus();
            scene.Time = Time;

            player = new MIDIPlayer();
            player.RunPlayer();
            player.Time = Time;

            speedSlider.nudToSlider = v => Math.Log(v, 2);
            speedSlider.sliderToNud = v => Math.Pow(2, v);
            sizeSlider.nudToSlider = v => Math.Log(v, 2);
            sizeSlider.sliderToNud = v => Math.Pow(2, v);

            speedSlider.Value = settings.Volatile.Speed;
            sizeSlider.Value = settings.Volatile.Size;

            versionLabel.Content = Settings.VersionName;

            d3d.FPSLock = settings.General.FPSLock;

            settings.General.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "FPSLock")
                    d3d.FPSLock = settings.General.FPSLock;
                if (e.PropertyName == "SelectedMIDIDevice")
                    player.DeviceID = settings.General.SelectedMIDIDevice;
                if (e.PropertyName == "CompatibilityFPS")
                    d3d.SingleThreadedRender = settings.General.CompatibilityFPS;
                if (e.PropertyName == "BackgroundColor")
                    glContainer.Background = new SolidColorBrush(settings.General.BackgroundColor);
                if(loadedFle != null)
                {
                    if (e.PropertyName == "PaletteName" || e.PropertyName == "PaletteRandomized")
                        loadedFle.SetColors(settings.PaletteSettings.Palettes[settings.General.PaletteName], settings.General.PaletteRandomized);
                }
            };

            d3d.FPSLock = settings.General.FPSLock;
            player.DeviceID = settings.General.SelectedMIDIDevice;
            d3d.SingleThreadedRender = settings.General.CompatibilityFPS;
            glContainer.Background = new SolidColorBrush(settings.General.BackgroundColor);

            Func<TimeSpan, string> timeSpanString = (s) => 
            s.Minutes + ":" + 
            (s.Seconds > 9 ? s.Seconds.ToString() : "0" + s.Seconds) + "." + 
            (s.Milliseconds - (s.Milliseconds % 100)) / 100;

            CompositionTarget.Rendering += (s, e) =>
            {
                var midiLen = loadedFle == null ? 0 : loadedFle.MidiLength;
                var midiTime = Time.GetTime();
                var renderText = timeSpanString(TimeSpan.FromSeconds(Time.GetTime())) + " / " + timeSpanString(TimeSpan.FromSeconds(midiLen)) + "\n" +
                                 "FPS: " + FPS.Value.ToString("#,##0.0") + "\n" +
                                 "Rendered Notes: " + scene.LastRenderedNoteCount.ToString("#,##0");

                if (midiTime > midiLen + 1) Time.Pause();

                double eventSkip = Math.Floor(player.BufferLen / 100.0);
                if (eventSkip > 0)
                {
                    audioDesyncLabel.Visibility = Visibility.Visible;
                    skipEventsCount.Content = "Skipping velocity: " + eventSkip;
                }
                else audioDesyncLabel.Visibility = Visibility.Hidden;

                renderInfo.Text = renderText;
                //renderNcLabel.Content = 
                timeSlider.Value = Time.GetTime();
                rotateLogo.Angle = timeSlider.Value * 4;
            };
        }
        public FPS FPS { get; set; }
        public PlayingState Time { get; set; }

        public void LoadMidi(string filename)
        {
            Time.Reset();
            player.File = null;
            scene.File = null;
            loadedFle = null;
            GC.Collect(2, GCCollectionMode.Forced);


            loadingForm = new LoadingMidiForm(filename, settings);
            loadingForm.ParseFinished += () =>
            {
                var file = loadingForm.LoadedFile;
                file.SetColors(settings.PaletteSettings.Palettes[settings.General.PaletteName], settings.General.PaletteRandomized);
                Dispatcher.Invoke(() =>
                {
                    player.File = file;
                    scene.File = file;
                    loadedFle = file;
                    timeSlider.Maximum = file.MidiLength;
                    loadingForm.Close();
                    loadingForm.Dispose();
                    loadingForm = null;
                    GC.Collect(2, GCCollectionMode.Forced);
                    Time.Play();
                });
            };
            loadingForm.ParseCancelled += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    loadingForm.Close();
                    loadingForm.Dispose();
                    GC.Collect(2, GCCollectionMode.Forced);
                    loadingForm = null;
                });
            };
            loadingForm.ShowDialog();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimiseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
            }
            catch { }
            WindowState = WindowState.Minimized;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            SetFullscreen(!Fullscreen);
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {

        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == WindowStateProperty)
            {
                if (WindowState != WindowState.Minimized)
                {
                    WindowStyle = WindowStyle.None;
                }
            }
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
        }

        private void TimeSlider_UserValueChanged(object sender, double e)
        {
            if (timeSlider == null) return;
            Time.Navigate(timeSlider.Value);
        }

        void PauseChanged()
        {
            if (Time.Paused)
            {
                pauseButton.Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
                playButton.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
            }
            else
            {
                pauseButton.Background = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                playButton.Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
            }
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Time.Paused)
                Time.Pause();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (scene.File != null)
                if (Time.Paused)
                    Time.Play();
        }

        private void MainWindow_PreviewDrop(object sender, DragEventArgs e)
        {
            if (!IsInitialized || loadingForm != null) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    Task.Run(() =>
                    {
                        Thread.Sleep(500);
                        Dispatcher.Invoke(() =>
                        {
                            dropHighlight.Visibility = Visibility.Hidden;
                            LoadMidi(files[0]);
                        });
                    });
                }
            }
        }

        private void MainWindow_PreviewDragEnter(object sender, DragEventArgs e)
        {
            if (!IsInitialized || loadingForm != null) return;
            dropHighlight.Visibility = Visibility.Visible;
        }

        private void MainWindow_PreviewDragLeave(object sender, DragEventArgs e)
        {
            if (!IsInitialized || loadingForm != null) return;
            dropHighlight.Visibility = Visibility.Hidden;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SetFullscreen(!Fullscreen);
            }
            if (e.Key == Key.Escape && Fullscreen)
            {
                SetFullscreen(!_fullscreen);
            }
            if (e.Key == Key.Right)
            {
                double time = Time.GetTime() + 5;
                time = Math.Max(time, timeSlider.Minimum);
                time = Math.Min(time, timeSlider.Maximum);
                Time.Navigate(time);
            }
            if (e.Key == Key.Left)
            {
                double time = Time.GetTime() - 5;
                time = Math.Max(time, timeSlider.Minimum);
                time = Math.Min(time, timeSlider.Maximum);
                Time.Navigate(time);
            }
            if (e.Key == Key.Space)
            {
                if (Time.Paused)
                {
                    if (scene.File != null) Time.Play();
                }
                else Time.Pause();
            }
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IsInitialized)
            {
                settings.Volatile.Speed = speedSlider.Value;
                Time.ChangeSpeed(settings.Volatile.Speed);
            }
        }

        private void SizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IsInitialized)
            {
                settings.Volatile.Size = sizeSlider.Value;
            }
        }

        private void OpenButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var open = new OpenFileDialog();
            open.Filter = "Midi files (*.mid)|*.mid";
            if ((bool)open.ShowDialog())
            {
                LoadMidi(open.FileName);
            }
        }

        private void SettingsButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (settingsWindow != null && settingsWindow.IsVisible) return;
            settingsWindow = new SettingsWindow(settings);
            settingsWindow.Owner = this;
            settingsWindow.Show();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            player.File = null;
            player.Dispose();
            scene.File = null;
            d3d.Dispose();
        }

        private void GlContainer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Keyboard.Focus(this);
            this.Focus();
            DependencyObject scope = FocusManager.GetFocusScope(this);
            FocusManager.SetFocusedElement(scope, this as IInputElement);
        }
    }
}
