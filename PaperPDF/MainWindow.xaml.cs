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
using System.Drawing;
using Color = System.Windows.Media.Color;
using System.Threading;
using System.Reflection;

namespace PaperPDF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string file_path = "";
        string note_path => file_path + ".notes";
        string dir;
        InkNoteSaveData inkNoteSaveData = new InkNoteSaveData();
        Setting setting;
        private SynchronizationContext Context { get; set; }
        public MainWindow()
        {
            InitializeComponent();

            dir = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var orgdir = Path.Combine(dir, "PaperPDF");
            if (!Directory.Exists(orgdir)) Directory.CreateDirectory(orgdir);
            dir = orgdir;

            if (File.Exists(Path.Combine(dir, "paper_pdf_config.yaml")))
            {
                try
                {
                    setting = new Deserializer().Deserialize<Setting>(File.ReadAllText(Path.Combine(dir, "paper_pdf_config.yaml")));
                }
                catch (Exception)
                {
                    setting = Setting.Default();
                    var result = MessageBox.Show("是否重置配置文件（否则退出）？", "PaperPDF配置文件错误", MessageBoxButton.YesNo);
                    if (result == MessageBoxResult.Yes)
                    {
                        if (File.Exists(Path.Combine(dir, "paper_pdf_config.old.yaml"))) File.Delete(Path.Combine(dir, "paper_pdf_config.old.yaml"));
                        File.Move(Path.Combine(dir, "paper_pdf_config.yaml"), Path.Combine(dir, "paper_pdf_config.old.yaml"));
                        File.WriteAllText(Path.Combine(dir, "paper_pdf_config.yaml"), new Serializer().Serialize(setting));
                    }
                    else
                    {
                        Application.Current.Shutdown();
                        return;
                    }

                }
            }
            else
            {
                setting = Setting.Default();
                File.WriteAllText(Path.Combine(dir, "paper_pdf_config.yaml"), new Serializer().Serialize(setting));
            }

            Context = SynchronizationContext.Current;

            string[] command = Environment.GetCommandLineArgs();//获取进程命令行参数



            scrollViewer.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(scrollViewer_MouseLeftButtonDown), true);
            scrollViewer.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(scrollViewer_MouseLeftButtonUp), true);

            MainInkCanvas.AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(MainInkCanvas_MouseLeftButtonDown), true);
            MainInkCanvas.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(MainInkCanvas_MouseLeftButtonUp), true);


            MainInkCanvas.DefaultDrawingAttributes.FitToCurve = true;

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
            ThreadPool.SetMaxThreads(1, 1);
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
            if (inkNoteSaveData.zoom <= 0.2f)
            {
                inkNoteSaveData.zoom = 1;
            }
            render_zoom = inkNoteSaveData.zoom;
            // 

            var index = 0;
            var top = 0.0;
            var scale = 1.0;
            foreach (var item in pdf.pages)
            {
                scale = 1000 / item.width;

                var image = new Image
                {
                    Width = item.width * scale * render_zoom,
                    Height = item.height * scale * render_zoom / 2,
                };
                var image2 = new Image
                {
                    Width = item.width * scale * render_zoom,
                    Height = item.height * scale * render_zoom / 2,
                };
                item.Top = top;
                MainInkCanvas.Children.Add(image);
                MainInkCanvas.Children.Add(image2);
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.LowQuality);
                RenderOptions.SetBitmapScalingMode(image2, BitmapScalingMode.LowQuality);
                InkCanvas.SetTop(image, top);
                InkCanvas.SetTop(image2, top + item.height * scale * render_zoom / 2);
                top += item.height * scale * render_zoom;
                index++;
            }
            pdf.scale = scale;
            MainInkCanvas.Width = render_zoom * 1000;
            MainInkCanvas.Height = top;



            ApplySchema();
        }

        void SetZoom(double zoom)
        {
            MainInkCanvas.Width = zoom * 1000;
            var scale = pdf.scale;

            double ver_offset = scrollViewer.VerticalOffset;
            double view_h = scrollViewer.ViewportHeight;

            double ra = (ver_offset + view_h / 2) / scrollViewer.ExtentHeight;

            double top = 0;
            for (int i = 0; i < pdf.pages.Count; i++)
            {
                var img = GetImageWithPage(i);
                var page = pdf.pages[i];
                img.Top.Width = zoom * scale * page.width;
                img.Top.Height = zoom * scale * page.height / 2;
                page.Top = top;
                InkCanvas.SetTop(img.Top, top);
                top += zoom * scale * page.height / 2;
                img.Bottom.Width = zoom * scale * page.width;
                img.Bottom.Height = zoom * scale * page.height / 2;
                InkCanvas.SetTop(img.Bottom, top);
                top += zoom * scale * page.height / 2;

                if (Math.Abs(pdf.last_render_zoom - zoom) >= 0.4)
                {
                    pdf.dirt = true;
                }
            }
            var ver_offset_new = top * ra - view_h / 2;
            scrollViewer.ScrollToVerticalOffset(ver_offset_new);
            MainInkCanvas.Height = top;
            var ma = new System.Windows.Media.Matrix();

            ma.ScaleAt(1 / render_zoom, 1 / render_zoom, 0, 0);
            //ma.Translate(-render_zoom / 2 * 1000, 0);
            ma.ScaleAt(zoom, zoom, 0, 0);
            MainInkCanvas.Strokes.Transform(ma, false);

            render_zoom = zoom;
            CheckInView(ver_offset_new, view_h);

        }


        MuPdf pdf;

        BitmapImage toBitmapImage(Bitmap bitmap, int i)
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
        }

        double render_zoom = 1;

        private static Semaphore renderMutex = new Semaphore(1, 1);
        public async void LoadPage((Image, Image) image, int i)
        {
            if (!pdf.pages[i].loading)
            {
                pdf.pages[i].loading = true;


                var bitmap = await Task.Run(() =>
                {
                    renderMutex.WaitOne();
                    if (pdf.pages[i].unload)
                    {
                        renderMutex.Release();
                        return (null, null);
                    }
                    // 不用信号量会内存访问冲突而导致崩溃
                    var res = pdf.RenderAPage(i, (float)render_zoom);
                    renderMutex.Release();
                    return res;
                });

                if(bitmap.Top == null)
                {
                    pdf.pages[i].loading = false;
                    return;
                }
                var bimage = await Task.Run(() => toBitmapImage(bitmap.Top, i));
                image.Item1.Source = bimage;
                var bimage2 = await Task.Run(() => toBitmapImage(bitmap.Bottom, i));
                image.Item2.Source = bimage2;
                if (pdf.pages[i].unload)
                {
                    image.Item1.Source = null;
                    image.Item2.Source = null;
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
            saveTimer = new System.Timers.Timer(60 * 1000);
            saveTimer.Elapsed += SaveTimer_Elapsed;
            saveTimer.AutoReset = true;
            saveTimer.Enabled = true;
        }

        System.Timers.Timer saveTimer;
        private void SaveTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            Context.Post((e) => { Save(); }, 0);
        }

        Button PreButton;
        void CheckPreButton()
        {
            if (PreButton != null)
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

        }

        public void CheckInView(double topoffset, double viewHeight)
        {
            var range = FindPage(topoffset, viewHeight);
            if (range[0] - 1 >= 0) range[0]--;
            if (range[1] + 1 < pdf.pages.Count) range[1]++;
            for (int i = 0; i < range[0]; i++)
            {
                var image = GetImageWithPage(i);
                image.Top.Source = null;
                image.Bottom.Source = null;
                pdf.pages[i].unload = true;
            }
            for (int i = range[0]; i <= range[1]; i++)
            {
                var image = GetImageWithPage(i);
                pdf.pages[i].unload = false;
                if (image.Top.Source == null || pdf.dirt)
                {
                    LoadPage(image, i);
                }
                pdf.last_render_zoom = render_zoom;
            }
            for (int i = range[1] + 1; i < pdf.pages.Count; i++)
            {
                var image = GetImageWithPage(i);
                image.Top.Source = null;
                image.Bottom.Source = null;
                pdf.pages[i].unload = true;
            }
            pdf.dirt = false;
        }

        public (Image Top, Image Bottom) GetImageWithPage(int page)
        {
            Image img, img2;

            img = MainInkCanvas.Children[page * 2] as Image;
            img2 = MainInkCanvas.Children[page * 2 + 1] as Image;

            return (img, img2);
        }

        public int[] FindPage(double offset, double viewHeight)
        {
            var res = new int[2] { 0, 0 };
            var Pages = pdf.pages;

            for (int i = 0; i < Pages.Count; i++)
            {
                var scale = pdf.scale;
                if (Pages[i].Top <= offset)
                {
                    res[0] = i;
                }
                if (Pages[i].Top + Pages[i].height * scale * render_zoom >= offset + viewHeight)
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

        System.Windows.Point mousePoint;
        private void scrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!inCanvas || MainInkCanvas.EditingMode == InkCanvasEditingMode.None)
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
            if (dragging && (!inCanvas || MainInkCanvas.EditingMode == InkCanvasEditingMode.None))
            {
                var curPosition = e.GetPosition(scrollViewer);
                var delta = curPosition - mousePoint;
                mousePoint = curPosition;
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - delta.Y * 1.5);
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - delta.X * 1.5);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Save();
        }

        void Save()
        {
            inkNoteSaveData.TopOffset = scrollViewer.VerticalOffset;
            inkNoteSaveData.zoom = render_zoom;
            SaveStrokes();
            SaveToFile();
        }

        void SaveStrokes()
        {
            var stm = new MemoryStream();
            MainInkCanvas.Strokes.Save(stm);
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

        private void Button_Click_4(object sender, RoutedEventArgs e)
        {
            // earse
            MainInkCanvas.EditingMode = InkCanvasEditingMode.EraseByStroke;
            CheckEditButton();
        }

        void onZoomIn(object sender, RoutedEventArgs e)
        {

            SetZoom(render_zoom + 0.1f);
        }

        void onZoomOut(object sender, RoutedEventArgs e)
        {
            var z = render_zoom - 0.1f;
            if (z <= 0.2f) z = 0.2f;
            SetZoom(z);
        }

        Button redonlyButton;
        void CheckEditButton()
        {
            if (MainInkCanvas.EditingMode == InkCanvasEditingMode.None)
            {
                redonlyButton.Content = "绘制";
            }
            else
            {
                redonlyButton.Content = "只读";
            }
        }
        void ToggleCanvasReadonly(object sender, RoutedEventArgs e)
        {
            if (MainInkCanvas.EditingMode != InkCanvasEditingMode.None)
            {
                MainInkCanvas.EditingMode = InkCanvasEditingMode.None;
                redonlyButton.Content = "绘制";
            }
            else
            {
                MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                redonlyButton.Content = "只读";
            }
        }

        void ApplySchema()
        {
            toolBar.Items.Clear();

            var redo = new Button()
            {
                Content = MainInkCanvas.EditingMode == InkCanvasEditingMode.None ? "只读" : "绘制"
            };
            redo.Click += ToggleCanvasReadonly;
            toolBar.Items.Add(redo);
            redonlyButton = redo;

            var pre = new Button()
            {
                Content = "上一个"
            };
            pre.Click += Button_Click;
            toolBar.Items.Add(pre);
            PreButton = pre;

            var bac = new Button()
            {
                Content = "下一个"
            };
            bac.Click += Button_Click_1;
            toolBar.Items.Add(bac);

            var lar = new Button()
            {
                Content = "大"
            };
            lar.Click += onZoomIn;
            toolBar.Items.Add(lar);

            var sma = new Button()
            {
                Content = "小"
            };
            sma.Click += onZoomOut;
            toolBar.Items.Add(sma);

            toolBar.Items.Add(new Separator());

            var schema = setting.Schemas.Find(v => v.Name == inkNoteSaveData.LastSchema);
            Schema sch;

            if (schema == null)
            {
                schema = setting.Schemas.Find(v => v.Name == setting.DefaultSchema);
                if (schema == null)
                {
                    if (setting.Schemas.Count > 0)
                    {
                        sch = setting.Schemas[0];
                    }
                    else
                    {
                        sch = new Schema();
                    }
                }
                else
                {
                    sch = schema;
                }

            }
            else
            {
                sch = schema;
            }
            inkNoteSaveData.LastSchema = sch.Name;

            bool addschema = false;
            foreach (var item in setting.Schemas)
            {
                if (item.Name != "Default")
                {
                    var button = new Button();
                    button.Content = item.Name;
                    button.Click +=
                         (object sender, RoutedEventArgs e) =>
                         {
                             inkNoteSaveData.LastSchema = item.Name;
                             ApplySchema();
                         };
                    toolBar.Items.Add(button);
                    addschema = true;
                }

            }
            if (addschema) toolBar.Items.Add(new Separator());

            var index = 0;
            if (index > sch.PenSettings.Count) index = 0;
            foreach (var item in sch.PenSettings)
            {
                var button = new Button();
                button.Content = item.Name;
                if (item.Type == "HighLight")
                {
                    RoutedEventHandler met =
                        (object sender, RoutedEventArgs e) =>
                        {
                            MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                            var da = new DrawingAttributes();
                            da.Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(item.Color);
                            da.IsHighlighter = true;
                            da.IgnorePressure = true;
                            da.StylusTip = StylusTip.Rectangle;
                            da.Height = 30;
                            da.Width = 10;
                            MainInkCanvas.DefaultDrawingAttributes = da;
                            inkNoteSaveData.LastUsePenIndex = index;
                            CheckEditButton();
                        };
                    button.Click += met;
                    if (index == inkNoteSaveData.LastUsePenIndex)
                    {
                        met.Invoke(null, null);
                    }
                }
                else if (item.Type == "Pen")
                {
                    RoutedEventHandler met =

                        (object sender, RoutedEventArgs e) =>
                        {
                            MainInkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                            var da = new DrawingAttributes();
                            da.Color = (Color)System.Windows.Media.ColorConverter.ConvertFromString(item.Color);
                            MainInkCanvas.DefaultDrawingAttributes = da;
                            inkNoteSaveData.LastUsePenIndex = index;
                            CheckEditButton();
                        };
                    button.Click += met;
                    if (index == inkNoteSaveData.LastUsePenIndex)
                    {
                        met.Invoke(null, null);
                    }
                }
                toolBar.Items.Add(button);

                index++;
            }
            var earse = new Button();
            earse.Click += Button_Click_4;
            earse.Content = "橡皮";
            toolBar.Items.Add(earse);
            MainInkCanvas.EditingMode = InkCanvasEditingMode.None;
        }


    }

    class InkNoteSaveData
    {
        public int Current;
        public double zoom;
        public double TopOffset;
        public string LastSchema;
        public int LastUsePenIndex;
        public List<InkNoteData> allnotes = new List<InkNoteData>();
    }
    class InkNoteData
    {
        public byte[] data;
    }

    class Setting
    {
        public string DefaultSchema;
        public List<Schema> Schemas = new List<Schema>();
        public static Setting Default()
        {
            var p = new Setting();
            var s = new Schema();

            s.PenSettings.Add(new PenSetting() { Type = "Pen", Color = "#000000", Name = "黑笔" });
            s.PenSettings.Add(new PenSetting() { Type = "Pen", Color = "#D52B2B", Name = "红笔" });
            s.PenSettings.Add(new PenSetting() { Type = "HighLight", Color = "#FFFF00", Name = "荧光笔" });
            p.Schemas.Add(s);
            s.Name = "Default";
            p.DefaultSchema = "Default";
            return p;
        }
    }

    class Schema
    {
        public string Name;
        public List<PenSetting> PenSettings = new List<PenSetting>();
    }
    class PenSetting
    {
        public string Type;
        public string Color;
        public string Name;
    }
}