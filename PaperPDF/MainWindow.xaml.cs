using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.IO;
using Image = System.Windows.Controls.Image;
using System.Windows.Input;
using System.Collections.Generic;
using YamlDotNet.Serialization;
using System;
using System.Windows.Ink;
using System.Windows.Media;

namespace PaperPDF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string file_path = "";
        string note_path => file_path + ".notes";
        InkNoteSaveData inkNoteSaveData = new InkNoteSaveData();
        public MainWindow()
        {
            InitializeComponent();


            scrollViewer.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(scrollViewer_MouseLeftButtonDown), true);
            scrollViewer.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(scrollViewer_MouseLeftButtonUp), true);

            MainInkCanvas.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(MainInkCanvas_MouseLeftButtonDown), true);
            MainInkCanvas.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(MainInkCanvas_MouseLeftButtonUp), true);


            MainInkCanvas.DefaultDrawingAttributes.FitToCurve = true;

            string[] command = Environment.GetCommandLineArgs();//获取进程命令行参数
            if (command.Length > 1)
            {
                file_path = command[1];
            }
            if (!file_path.EndsWith(".pdf"))
            {
                MessageBox.Show("无法打开非PDF文件");
                App.Current.Shutdown();
            }

            pdf = new MuPdf(file_path);
            // read note data
            if (File.Exists(note_path))
            {
                try
                {
                    inkNoteSaveData = new Deserializer().Deserialize<InkNoteSaveData>(File.ReadAllText(note_path));
                }
                catch (System.Exception)
                {
                    MessageBox.Show("笔记打开失败！");
                }
            }
            else
            {
                inkNoteSaveData = new InkNoteSaveData();
            }
            if (inkNoteSaveData.allnotes.Count == 0) { inkNoteSaveData.allnotes.Add(new InkNoteData()); }

            // 

            var index = 0;
            var top = 0.0;
            foreach (var item in pdf.pages)
            {
                var scale = MainInkCanvas.Width / item.width;

                var image = new Image
                {
                    Width = item.width * scale,
                    Height = item.height * scale,
                };
                item.scale = scale;
                item.Top = top;
                MainInkCanvas.Children.Add(image);
                InkCanvas.SetTop(image, top);
                top += item.height * scale;
                index++;
            }

        }


        MuPdf pdf;

        public async Task LoadPage(Image image, int i)
        {
            if (!pdf.pages[i].loading)
            {
                pdf.pages[i].loading = true;
                var bitmap = pdf.RenderAPage(i);
                var bimage = await Task.Run(() =>
               {
                   if (!pdf.pages[i].unload)
                   {

                       MemoryStream ms = new MemoryStream();
                       bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
                       byte[] bytes = ms.GetBuffer();  //byte[]   bytes=   ms.ToArray(); 这两句都可以
                       ms.Close();

                       //Convert it to BitmapImage
                       var newms = new MemoryStream(bytes);
                       BitmapImage bimage = new BitmapImage();
                       bimage.BeginInit();
                       bimage.StreamSource = newms;
                       bimage.EndInit();

                       bimage.Freeze();
                       bitmap.Dispose();
                       return bimage;
                   }
                   return null;
               });

                if (!pdf.pages[i].unload)
                {
                    image.Source = bimage;
                }

                pdf.pages[i].loading = false;
            }

        }

        bool loaded = false;
        private void MainInkCanvas_Loaded(object sender, RoutedEventArgs e)
        {
            CheckInView(0, scrollViewer.ViewportHeight);
            loaded = true;
            if (inkNoteSaveData.allnotes[inkNoteSaveData.Current].data != null)
            {
                MainInkCanvas.Strokes = new StrokeCollection(new MemoryStream(inkNoteSaveData.allnotes[inkNoteSaveData.Current].data));
            }
            CheckPreButton();
            scrollViewer.ScrollToVerticalOffset(inkNoteSaveData.TopOffset);

        }

        void CheckPreButton()
        {
            if (inkNoteSaveData.Current == 0)
            {
                PreButton.IsEnabled = false;
            }
            else
            {
                PreButton.IsEnabled = true;
            }
        }

        public void CheckInView(double topoffset, double viewHeight)
        {

            var range = FindPage(topoffset, viewHeight);
            if (range[0] - 1 >= 0) range[0]--;
            if (range[1] + 1 < pdf.pages.Count) range[1]++;
            for (int i = 0; i < range[0]; i++)
            {
                var image = GetImageWithPage(i);
                image.Source = null;
                pdf.pages[i].unload = true;
            }
            for (int i = range[0]; i <= range[1]; i++)
            {
                var image = GetImageWithPage(i);
                pdf.pages[i].unload = false;
                if (image.Source == null)
                {
                    LoadPage(image, i);
                }
            }
            for (int i = range[1] + 1; i < pdf.pages.Count; i++)
            {
                var image = GetImageWithPage(i);
                image.Source = null;
                pdf.pages[i].unload = true;
            }

        }

        public Image GetImageWithPage(int page)
        {
            Image img;
            page--;
            do
            {
                page++;
                img = MainInkCanvas.Children[page] as Image;
            } while (img == null);
            return img;
        }

        public int[] FindPage(double offset, double viewHeight)
        {
            var res = new int[2] { int.MaxValue, 0 };
            var Pages = pdf.pages;

            for (int i = 0; i < Pages.Count; i++)
            {
                var scale = Pages[i].scale;
                if (Pages[i].Top <= offset && Pages[i].Top + Pages[i].height * scale >= offset)
                {
                    res[0] = i;
                }
                if (Pages[i].Top + Pages[i].height * scale >= offset + viewHeight)
                {
                    res[1] = i;
                    break;
                }
            }
            return res;
        }

        private void scrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (loaded)
            {
                var top = e.VerticalOffset;
                CheckInView(top, e.ViewportHeight);
            }

        }

        Point mousePoint;
        private void scrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!inCanvas)
            {
                dragging = true;
                mousePoint = e.GetPosition(scrollViewer);
            }
        }

        bool inCanvas = false;
        bool dragging = false;
        private void MainInkCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            inCanvas = true;
        }

        private void MainInkCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            inCanvas = false;
        }


        private void scrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            dragging = false;
            inCanvas = false;
        }

        private void PdfGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (dragging)
            {
                dragging = false;
                inCanvas = false;
            }
        }

        private void scrollViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (dragging && !inCanvas)
            {
                var curPosition = e.GetPosition(scrollViewer);
                var delta = curPosition - mousePoint;
                mousePoint = curPosition;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - delta.Y * 1.5);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            inkNoteSaveData.TopOffset = scrollViewer.VerticalOffset;
            SaveStrokes();
            SaveToFile();
        }

        void SaveStrokes()
        {
            var stokes = MainInkCanvas.Strokes;
            var stm = new MemoryStream();
            stokes.Save(stm);
            inkNoteSaveData.allnotes[inkNoteSaveData.Current].data = stm.ToArray();
            stm.Close();
        }

        void SaveToFile()
        {
            var ser = new Serializer();
            var str = ser.Serialize(inkNoteSaveData);
            File.WriteAllText(note_path, str);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // pre
            SaveStrokes();
            inkNoteSaveData.Current -= 1;
            if (inkNoteSaveData.Current < 0) inkNoteSaveData.Current = 0;
            MainInkCanvas.Strokes.Clear();
            if (inkNoteSaveData.allnotes[inkNoteSaveData.Current].data != null)
                MainInkCanvas.Strokes = new StrokeCollection(new MemoryStream(inkNoteSaveData.allnotes[inkNoteSaveData.Current].data));

            CheckPreButton();
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            // next
            SaveStrokes();
            inkNoteSaveData.Current += 1;
            if (inkNoteSaveData.Current >= inkNoteSaveData.allnotes.Count)
            {
                inkNoteSaveData.allnotes.Add(new InkNoteData());
            }
            MainInkCanvas.Strokes.Clear();
            if (inkNoteSaveData.allnotes[inkNoteSaveData.Current].data != null)
                MainInkCanvas.Strokes = new StrokeCollection(new MemoryStream(inkNoteSaveData.allnotes[inkNoteSaveData.Current].data));

            CheckPreButton();
        }

        private void Button_Click_2(object sender, RoutedEventArgs e)
        {
            // black
            MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            var da = new DrawingAttributes();
            da.Color = Colors.Black;
            MainInkCanvas.DefaultDrawingAttributes = da;
        }

        private void Button_Click_3(object sender, RoutedEventArgs e)
        {
            // red
            MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            var da = new DrawingAttributes();
            da.Color = Colors.Red;
            MainInkCanvas.DefaultDrawingAttributes = da;
        }

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            // earse
            MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
        }

        private void Button_Click_5(object sender, RoutedEventArgs e)
        {
            // 荧光笔
            MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            var da = new DrawingAttributes();
            da.Color = Colors.Yellow;
            da.IsHighlighter = true;
            da.IgnorePressure = true;
            da.StylusTip = StylusTip.Rectangle;
            da.Height = 30;
            da.Width = 10;
            MainInkCanvas.DefaultDrawingAttributes = da;
        }
    }

    class InkNoteSaveData
    {
        public int Current;
        public double TopOffset;
        public List<InkNoteData> allnotes = new List<InkNoteData>();
    }
    class InkNoteData
    {
        public byte[] data;
    }
}