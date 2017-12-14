using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using AForge.Video.DirectShow;
using AForge.Video.FFMPEG;
using System.Windows.Media.Effects;
using System.Globalization;

namespace AzCam
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            IconHelper.RemoveIcon(this);
        }

        private bool DeviceExist = false;
        private FilterInfoCollection videoDevices;
        private VideoCaptureDevice videoSource = null;

        private void getCamList()
        {
            videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);

            if (videoDevices.Count == 0)
                throw new ApplicationException();

            videoSourceComboBox.Items.Clear();

            foreach (FilterInfo vd in videoDevices)
            {
                var cbi = new ComboBoxItem();
                cbi.Content = vd.Name;
                cbi.Tag = vd.MonikerString;
                videoSourceComboBox.Items.Add(cbi);
            }

            videoSourceComboBox.SelectedIndex = 0;

            DeviceExist = true;
        }

        Thread ServerThread;

        bool isRunning = true;

        TcpListener server;

        void ServerLoop()
        {
            server = new TcpListener(IPAddress.Loopback, 28934);
            server.Start();

            while (isRunning)
            {
                server.AcceptTcpClient();

                captureButton.Dispatcher.Invoke(delegate()
                {
                    captureButton_MouseDown(this, new MouseButtonEventArgs(InputManager.Current.PrimaryMouseDevice, 0, MouseButton.Left));
                });
            }
        }

        Storyboard myStoryboard = new Storyboard();
        DoubleAnimation myDoubleAnimation = new DoubleAnimation();
        BlurEffect blurEffect = new BlurEffect();             

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FrameCounter.Instance.FPSCalculatedEvent += Instance_FPSCalculatedEvent;
            this.RegisterName("blurEffect", blurEffect);
            blurEffect.Radius = 0.0;
            webcamImage.Effect = blurEffect;

            myDoubleAnimation.From = 0;
            myDoubleAnimation.To = 35;
            myDoubleAnimation.Duration = new Duration(TimeSpan.FromSeconds(0.2));

            Storyboard.SetTargetName(myDoubleAnimation, "blurEffect");
            Storyboard.SetTargetProperty(myDoubleAnimation, new PropertyPath(BlurEffect.RadiusProperty));
            myStoryboard.Children.Add(myDoubleAnimation);

            captureButtonOpacityThread = new Thread(captureButtonOpacityHandler);
            captureButtonOpacityThread.Start();

            ServerThread = new Thread(ServerLoop);
            ServerThread.Start();

            getCamList();
        }

        string fps = "0 FPS";

        void Instance_FPSCalculatedEvent(string fps)
        {
            this.fps = fps.Replace("fps: ", "");
        }

        void setHighestResolution()
        {
            if (videoSource.VideoCapabilities.Length > 0)
            {
                int largestWidth = 0;

                int lwi = 0;

                for (int i = 0; i < resolutionComboBox.Items.Count; i++)
                {
                    string itemstr = resolutionComboBox.Items[i].ToString();

                    int width = Int32.Parse(itemstr.Split('x')[0]);
                    int height = Int32.Parse(itemstr.Split('x')[1].Split(',')[0]);

                    int fps = Int32.Parse(itemstr.Split('x')[1].Split(',')[1].Remove(0, 1).Split(' ')[0]);

                    if (width > largestWidth)
                    {
                        videoSource.VideoResolution = videoSource.VideoCapabilities.Where(vcr => vcr.FrameSize.Equals(new System.Drawing.Size(width, height)) && vcr.MaximumFrameRate == fps).First();
                        largestWidth = width;
                        lwi = i;
                    }
                }

                resolutionComboBox.SelectedIndex = lwi;

                //videoSource.VideoResolution = videoSource.VideoCapabilities[0];

                //
            }
        }

        void ConnectToCamera()
        {
            resolutionComboBox.Items.Clear();

            foreach (VideoCapabilities capabilities in videoSource.VideoCapabilities)
            {
                resolutionComboBox.Items.Add(capabilities.FrameSize.Width + "x" + capabilities.FrameSize.Height + ", " + capabilities.MaximumFrameRate.ToString() + " FPS");
            }

            setHighestResolution();

            BindFunctions();
        }

        void videoSource_NewFrame(object sender, AForge.Video.NewFrameEventArgs eventArgs)
        {
            webcamImage.Dispatcher.Invoke(delegate() {
                if (webcamImage.Width != eventArgs.Frame.Size.Width || webcamImage.Height != eventArgs.Frame.Size.Height)
                {
                    webcamImage.Width = eventArgs.Frame.Size.Width;
                    webcamImage.Height = eventArgs.Frame.Size.Height;
                }
            });

            if (isRecording)
            {
                FrameQueue.Enqueue(new VideoFrame((Bitmap)eventArgs.Frame.Clone(), DateTime.Now - videoStart));
            }

            BitmapImage bi = new BitmapImage();
            bi.BeginInit();

            MemoryStream ms = new MemoryStream();
            eventArgs.Frame.Save(ms, ImageFormat.Bmp);
            ms.Seek(0, SeekOrigin.Begin);

            bi.StreamSource = ms;
            bi.EndInit();

            //Using the freeze function to avoid cross thread operations 
            bi.Freeze();

            //Calling the UI thread using the Dispatcher to update the 'Image' WPF control         
            webcamImage.Dispatcher.Invoke(delegate
            {
                webcamImage.Source = bi; /*frameholder is the name of the 'Image' WPF control*/
                FrameCounter.Instance.Count();
                frame_counter++;

                fpsLabel.Content = fps + " FPS";

                resolutionLabel.Content = videoSource.VideoResolution.FrameSize.Width + "x" + videoSource.VideoResolution.FrameSize.Height + ", " + videoSource.VideoResolution.AverageFrameRate + " FPS";

                cameraIDLabel.Content = videoSource.Source;

                debugGrid.Width = MeasureString(videoSource.Source).Width+5;

                framesLabel.Content = frame_counter + " frames decoded";

                if (isRecording)
                {
                    TimeSpan recordTimeElapsed = DateTime.Now - videoStart;

                    string time = "";

                    if (recordTimeElapsed.Hours > 0)
                        time += recordTimeElapsed.Hours + ":";

                    time += recordTimeElapsed.Minutes + ":";

                    if (recordTimeElapsed.Seconds < 10)
                        time += "0";

                    time += recordTimeElapsed.Seconds.ToString();

                    recordTimeLabel.Content = time;
                }
            });

            GC.Collect();

            

        }

        int frame_counter = 0;

        private System.Windows.Size MeasureString(string candidate)
        {
            var formattedText = new FormattedText(
                candidate,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(cameraIDLabel.FontFamily, cameraIDLabel.FontStyle, cameraIDLabel.FontWeight, cameraIDLabel.FontStretch),
                cameraIDLabel.FontSize,
                System.Windows.Media.Brushes.Black);

            return new System.Windows.Size(formattedText.Width, formattedText.Height);
        }



        public static BitmapSource ToWpfBitmap(System.Drawing.Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);

                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                // According to MSDN, "The default OnDemand cache option retains access to the stream until the image is needed."
                // Force the bitmap to load right now so we can dispose the stream.
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        private void CloseVideoSource()
        {
            if (!(videoSource == null))
                if (videoSource.IsRunning)
                {
                    videoSource.SignalToStop();
                    videoSource = null;
                }
        }

        DateTime lastImageTaken = DateTime.FromFileTimeUtc(0);

        DateTime videoStart;

        VideoFileWriter vfi;

        bool isRecording = false;

        private void captureButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (currentMode == 0)
            {
                if ((DateTime.Now - lastImageTaken).TotalMilliseconds > 200)
                {
                    lastImageTaken = DateTime.Now;

                    BitmapSource captureImage = (BitmapSource)webcamImage.Source;

                    /*CapturedImage.Source = BitmapImage.Create(
                        2,
                        2,
                        96,
                        96,
                        PixelFormats.Indexed1,
                        new BitmapPalette(new List<Color> { Colors.Transparent }),
                        new byte[] { 0, 0, 0, 0 },
                        1);*/
                    CapturedImage.Source = captureImage;

                    CapturedImage.Width = captureImage.Width;
                    CapturedImage.Height = captureImage.Height;
                    CapturedImage.Opacity = 1;

                    /*DoubleAnimation da = new DoubleAnimation();
                    da.From = videoSource.VideoResolution.FrameSize.Height;
                    da.To = new Thickness(100);
                    da.Duration = new Duration(TimeSpan.FromSeconds(0.15F));
                     * */

                    //da.RepeatBehavior=new RepeatBehavior(3);
                    DoubleAnimation ta = new DoubleAnimation();
                    ta.From = captureImage.Height;
                    ta.To = 0;
                    ta.Duration = new Duration(TimeSpan.FromSeconds(0.3F));
                    ta.Completed += ta_Completed;

                    CapturedImage.BeginAnimation(HeightProperty, ta);

                    DoubleAnimation da = new DoubleAnimation();
                    da.From = 1;
                    da.To = 0;
                    da.Duration = new Duration(TimeSpan.FromSeconds(0.2F));

                    CapturedImage.BeginAnimation(OpacityProperty, da);

                    WriteJpeg(GetFileName("Picture", ".jpeg"), 100, (BitmapSource)captureImage, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
                }
            }
            else if (currentMode == 1)
            {
                if (isRecording)
                {
                    StopRecording();
                }
                else
                {
                    StartRecording();
                }
            }
        }

        void StartRecording()
        {
            captureButton.Source = new BitmapImage(new Uri("pack://application:,,,/AzCam;component/Resources/aperture-recording.png", UriKind.Absolute));

            videoStart = DateTime.Now;

            vfi = new VideoFileWriter();
            //vfi.Open(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) + "/" + GetFileName("Video", ".avi"), videoSource.VideoResolution.FrameSize.Width, videoSource.VideoResolution.FrameSize.Height, 30, VideoCodec.MPEG4, 8000000);

            vfi.Open("D:/" + GetFileName("Video", ".avi"), videoSource.VideoResolution.FrameSize.Width, videoSource.VideoResolution.FrameSize.Height, 30, VideoCodec.MPEG4, 8000000);

            isRecording = true;

            FrameQueueHandler = new Thread(HandleFrameQueue);
            FrameQueueHandler.Start();

            DoubleAnimation da = new DoubleAnimation();
            da.To = 1;
            da.Duration = new Duration(TimeSpan.FromSeconds(0.3F));

            recordTimeLabel.BeginAnimation(OpacityProperty, da);
        }

        void StopRecording()
        {
            isRecording = false;
            FrameQueueHandler.Join();

            vfi.Close();

            captureButton.BeginAnimation(OpacityProperty, null);
            captureButton.Source = new BitmapImage(new Uri("pack://application:,,,/AzCam;component/Resources/Aperture.png", UriKind.Absolute));

            DoubleAnimation da = new DoubleAnimation();
            da.To = 0;
            da.Duration = new Duration(TimeSpan.FromSeconds(0.3F));

            recordTimeLabel.BeginAnimation(OpacityProperty, da);
        }

        Thread FrameQueueHandler;

        Thread captureButtonOpacityThread;

        // false = --
        // true  = ++
        bool cBoADirection = false;

        void captureButtonOpacityHandler()
        {
            while (true)
            {
                if (isRecording)
                {
                    captureButton.Dispatcher.Invoke(delegate()
                    {
                        captureButton.BeginAnimation(OpacityProperty, null);

                        if (!cBoADirection)
                        {
                            if (captureButton.Opacity >= 0.3)
                            {
                                captureButton.Opacity-=0.005;
                            }
                            else
                                cBoADirection = true;
                        }
                        else
                        {
                            if (captureButton.Opacity <= 1)
                            {
                                captureButton.Opacity+=0.005;
                            }
                            else
                                cBoADirection = false;
                        }
                    });

                    Thread.Sleep(5);
                }
                else
                {
                    Thread.Sleep(200);
                }
            }
        }

        Queue<VideoFrame> FrameQueue = new Queue<VideoFrame>();

        void HandleFrameQueue()
        {
            while (isRecording)
            {
                if (FrameQueue.Count > 0)
                {
                    VideoFrame frame = FrameQueue.Dequeue();

                    if (frame.Image.Size.Width != vfi.Width || frame.Image.Size.Height != vfi.Height)
                    {
                        Bitmap resizedBmp = (Bitmap)frame.Image.GetThumbnailImage(vfi.Width, vfi.Height, null, IntPtr.Zero);

                        frame.Image.Dispose();

                        frame.Image = resizedBmp;
                    }

                    vfi.WriteVideoFrame(frame.Image, frame.Time);

                    frame.Dispose();
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        static string GetFileName(string context, string extension)
        {
            return context + "_" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss.ffff") + extension;
        }

        static FileStream CreateFileWithUniqueName(string folder, string fileName,
        int maxAttempts = 1024)
        {
            // get filename base and extension
            var fileBase = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);
            // build hash set of filenames for performance
            var files = new HashSet<string>(Directory.GetFiles(folder));

            for (var index = 0; index < maxAttempts; index++)
            {
                // first try with the original filename, else try incrementally adding an index
                var name = (index == 0)
                    ? fileName
                    : String.Format("{0} ({1}){2}", fileBase, index, ext);

                // check if exists
                var fullPath = System.IO.Path.Combine(folder, name);
                if (files.Contains(fullPath))
                    continue;

                // try to open the stream
                try
                {
                    return new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write);
                }
                catch (DirectoryNotFoundException) { throw; }
                catch (DriveNotFoundException) { throw; }
                catch (IOException) { } // ignore this and try the next filename
            }

            throw new Exception("Could not create unique filename in " + maxAttempts + " attempts");
        }

        static void WriteJpeg(string fileName, int quality, BitmapSource bmp, string folder)
        {

            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(ResizeBitmap(bmp, bmp.Width*2.25, bmp.Height*2.25), null, null, null));
            encoder.QualityLevel = quality;

            using (FileStream file = CreateFileWithUniqueName(folder, fileName))
            {
                encoder.Save(file);
            }
        }

        public static BitmapSource ResizeBitmap(BitmapSource source, double nWidth, double nHeight)
        {
            TransformedBitmap tbBitmap = new TransformedBitmap(source,
                                                      new ScaleTransform(nWidth / source.PixelWidth,
                                                                         nHeight / source.PixelHeight,
                                                                         0, 0));
            return tbBitmap;
        }

        void ta_Completed(object sender, EventArgs e)
        {
            Dispatcher.Invoke(delegate()
            {
                ThicknessAnimation ta = new ThicknessAnimation();
                ta.From = new Thickness(0);
                ta.To = new Thickness(0, 0, 0, 0);
                ta.Duration = new Duration(TimeSpan.FromSeconds(0));
                ta.Completed += ta_FixCompleted;

                CapturedImage.BeginAnimation(MarginProperty, ta);
                    
            });

        }

        void ta_FixCompleted(object sender, EventArgs e)
        {
            
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (captureButtonOpacityThread != null)
            {
                captureButtonOpacityThread.Abort();
                captureButtonOpacityThread.Join();
            }

            if (FrameQueueHandler != null)
            {
                StopRecording();
            }

            CloseVideoSource();

            isRunning = false;

            server.Stop();

            ServerThread.Abort();
            ServerThread.Join();
        }

        int currentMode = 0;

        private void modeButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (currentMode == 0)
            {
                currentMode = 1;

                modeButton.Source = new BitmapImage(new Uri("pack://application:,,,/AzCam;component/Resources/record.png", UriKind.Absolute));
            }
            else
            {
                if (isRecording)
                    StopRecording();

                currentMode = 0;

                modeButton.Source = new BitmapImage(new Uri("pack://application:,,,/AzCam;component/Resources/picture.png", UriKind.Absolute));
            }
        }

        bool optionsOpen = false;

        void CloseOptions()
        {
            optionsOpen = false;
            // close

            myDoubleAnimation.To = 0;

            DoubleAnimation da = new DoubleAnimation();
            da.To = 1;
            da.Duration = new Duration(TimeSpan.FromSeconds(0.2F));

            webcamImage.BeginAnimation(OpacityProperty, da);

            DoubleAnimation da2 = new DoubleAnimation();
            da2.To = 0;
            da2.Duration = new Duration(TimeSpan.FromSeconds(0.1F));
            da2.Completed += da2_Completed;

            optionsCanvas.BeginAnimation(OpacityProperty, da2);

            myStoryboard.Begin(this);
        }

        private void optionsButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (optionsOpen)
            {
                CloseOptions();
            }
            else
            {
                optionsOpen = true;

                myDoubleAnimation.To = 45;

                DoubleAnimation da = new DoubleAnimation();
                da.To = 0.4;
                da.Duration = new Duration(TimeSpan.FromSeconds(0.2F));

                webcamImage.BeginAnimation(OpacityProperty, da);

                DoubleAnimation da2 = new DoubleAnimation();
                da2.To = 0.7;
                da2.Duration = new Duration(TimeSpan.FromSeconds(0.2F));
                optionsCanvas.Visibility = System.Windows.Visibility.Visible;

                optionsCanvas.BeginAnimation(OpacityProperty, da2);

                myStoryboard.Begin(this);
            }
        }

        void da2_Completed(object sender, EventArgs e)
        {
            optionsCanvas.Visibility = System.Windows.Visibility.Hidden;
        }

        private void resolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (resolutionComboBox.Items.Count > 0)
            {
                //this.SizeToContent = System.Windows.SizeToContent.WidthAndHeight;

                string itemstr = resolutionComboBox.SelectedItem.ToString();

                    int width = Int32.Parse(itemstr.Split('x')[0]);
                    int height = Int32.Parse(itemstr.Split('x')[1].Split(',')[0]);

                    int fps = Int32.Parse(itemstr.Split('x')[1].Split(',')[1].Remove(0, 1).Split(' ')[0]);
                    videoSource.Stop();

                    string oldid = videoSource.Source;

                    videoSource = null;

                    videoSource = new VideoCaptureDevice(oldid);

                    videoSource.VideoResolution = videoSource.VideoCapabilities.Where(vcr => vcr.FrameSize.Equals(new System.Drawing.Size(width, height)) && vcr.MaximumFrameRate == fps).First();

                    BindFunctions();
            }
        }

        private void CenterWindowOnScreen()
        {
            double screenWidth = System.Windows.SystemParameters.PrimaryScreenWidth;
            double screenHeight = System.Windows.SystemParameters.PrimaryScreenHeight;
            double windowWidth = webcamImage.Width;
            double windowHeight = webcamImage.Height;
            this.Left = (screenWidth / 2) - (windowWidth / 2);
            this.Top = (screenHeight / 2) - (windowHeight / 2);
        }

        private void window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.SizeToContent = System.Windows.SizeToContent.WidthAndHeight;
        }

        private void videoSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (videoSource != null)
            {
                videoSource.NewFrame -= null;
                videoSource.Stop();

                //videoSource.WaitForStop();
                videoSource = null;
            }
            ComboBoxItem cbi = (ComboBoxItem)videoSourceComboBox.SelectedItem;
            videoSource = new VideoCaptureDevice(cbi.Tag.ToString());
            //CloseVideoSource();
            ConnectToCamera();
        }

        private void window_MouseEnter(object sender, MouseEventArgs e)
        {
            
        }

        private void Grid_MouseEnter(object sender, MouseEventArgs e)
        {
            //Title = "MouseEnter " + DateTime.Now.Ticks;
        }

        void BindFunctions()
        {
            videoSource.NewFrame += videoSource_NewFrame;

            try
            {
                string itemstr = "";
                int width = 0;
                int height = 0;

                resolutionComboBox.Dispatcher.Invoke(delegate()
                {
                    itemstr = resolutionComboBox.SelectedItem.ToString();

                    width = Int32.Parse(itemstr.Split('x')[0]);
                    height = Int32.Parse(itemstr.Split('x')[1].Split(',')[0]);

                });


                webcamImage.Dispatcher.Invoke(delegate()
                {
                    webcamImage.Width = width;
                    webcamImage.Height = height;
                });
                
                
            }
            catch
            {

            }

            //videoSource.DesiredFrameRate = 10;
            videoSource.Start();
        }

        private void showDebugInfoCheckbox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (showDebugInfoCheckbox.IsChecked == true)
            {
                debugGrid.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                debugGrid.Visibility = System.Windows.Visibility.Hidden;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            CloseOptions();
            videoSource.DisplayPropertyPage(new WindowInteropHelper(this).Handle);
        }
    }

    public static class IconHelper
    {
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter,
                   int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, uint msg,
                   IntPtr wParam, IntPtr lParam);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_DLGMODALFRAME = 0x0001;
        const int SWP_NOSIZE = 0x0001;
        const int SWP_NOMOVE = 0x0002;
        const int SWP_NOZORDER = 0x0004;
        const int SWP_FRAMECHANGED = 0x0020;
        const uint WM_SETICON = 0x0080;

        public static void RemoveIcon(Window window)
        {
            // Get this window's handle
            IntPtr hwnd = new WindowInteropHelper(window).Handle;

            // Change the extended window style to not show a window icon
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_DLGMODALFRAME);

            // Update the window's non-client area to reflect the changes
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE |
                  SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

    }

    public class FrameCounter
    {
        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceCounter(
            out long lpPerformanceCount);

        [DllImport("Kernel32.dll")]
        private static extern bool QueryPerformanceFrequency(
            out long lpFrequency);

        #region Singleton Pattern
        private static FrameCounter instance = null;
        public static FrameCounter Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new FrameCounter();
                }
                return instance;
            }
        }
        #endregion

        #region Constructor
        private FrameCounter()
        {
            msPerTick = (float)MillisecondsPerTick;
        }
        #endregion

        float msPerTick = 0.0f;

        long frequency;
        public long Frequency
        {
            get
            {
                QueryPerformanceFrequency(out frequency);
                return frequency;
            }
        }

        long counter;
        public long Counter
        {
            get
            {
                QueryPerformanceCounter(out counter);
                return counter;
            }
        }

        public double MillisecondsPerTick
        {
            get
            {
                return (1000L) / (double)Frequency;
            }
        }

        public delegate void FPSCalculatedHandler(string fps);
        public event FPSCalculatedHandler FPSCalculatedEvent;

        long now;
        long last;
        long dc;
        float dt;
        float elapsedMilliseconds = 0.0f;
        int numFrames = 0;
        float msToTrigger = 1000.0f;

        public float Count()
        {
            last = now;
            now = Counter;
            dc = now - last;
            numFrames++;

            dt = dc * msPerTick;

            elapsedMilliseconds += dt;

            if (elapsedMilliseconds > msToTrigger)
            {
                float seconds = elapsedMilliseconds / 1000.0f;
                float fps = numFrames / seconds;

                if (FPSCalculatedEvent != null)
                    FPSCalculatedEvent("fps: " + fps.ToString("0.00"));

                elapsedMilliseconds = 0.0f;
                numFrames = 0;
            }

            return dt;
        }
    }
}
