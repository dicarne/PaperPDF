#nullable enable
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PaperPDF
{
    class PageRef
    {
        public IntPtr pagePtr;
        public int ID;
        public float width, height;
        public double Top;
        public bool loading;
        public bool unload;
    }
    class MuPdf
    {
        public double last_render_zoom;
        public bool dirt;

        IntPtr ctx;
        IntPtr doc;
        IntPtr stm;
        public List<PageRef> pages;
        public double scale;
        public MuPdf(string path)
        {
            pages = new List<PageRef>();
            const uint FZ_STORE_DEFAULT = 256 << 20;
            ctx = NativeMethods.NewContext(IntPtr.Zero, IntPtr.Zero, FZ_STORE_DEFAULT, "1.17.0"); // 创建上下文
            NativeMethods.fz_register_document_handlers(ctx);
            stm = NativeMethods.OpenFile(ctx, path); // 打开文件流
            doc = NativeMethods.OpenDocumentStream(ctx, ".pdf", stm); // 从文件流创建文档对象
            int pn = NativeMethods.CountPages(ctx, doc); // 获取文档的页数
            for (int i = 0; i < pn; i++)
            {
                // 遍历各页
                IntPtr p = NativeMethods.LoadPage(ctx, doc, i); // 加载页面（首页为 0）
                Rectangle b = NativeMethods.BoundPage(ctx, p); // 获取页面尺寸
                int width = (int)(b.Right - b.Left); // 获取页面的宽度和高度
                int height = (int)(b.Bottom - b.Top);
                pages.Add(new PageRef()
                {
                    ID = i,
                    pagePtr = p,
                    width = width,
                    height = height
                });
            }
            unsafe
            {
                // 在此读取目录
                fz_outline* ptr = NativeMethods.fz_load_outline(ctx, doc); ;
                outlines = readOutline(ptr);
            }
            BindMetadata();

        }

        /// <summary>
        /// 读取目录
        /// </summary>
        /// <param name="ptr"></param>
        /// <returns></returns>
        unsafe List<OutlineIndex> readOutline(fz_outline* ptr)
        {
            var col = new List<OutlineIndex>();
            while (ptr != null)
            {
                var obj = new OutlineIndex()
                {
                    page = ptr->page,
                    x = ptr->x,
                    y = ptr->y,
                    refs = ptr->refs,
                    title = ReadCStr(ptr->title),
                    uri = ReadCStr(ptr->uri)
                };

                if (ptr->down != null)
                {
                    obj.down = readOutline(ptr->down);
                }
                col.Add(obj);
                ptr = ptr->next;
            }
            return col;
        }


        unsafe string ReadCStr(byte* buf)
        {
            var str = new List<byte>(20);
            while (*buf != 0)
            {
                str.Add(*buf);
                buf++;
            }
            return Encoding.UTF8.GetString(str.ToArray());
        }

        public List<OutlineIndex> outlines = new();

        /// <summary>
        /// 渲染一页
        /// </summary>
        /// <param name="pageID"></param>
        /// <param name="zoom"></param>
        /// <returns></returns>
        public (Bitmap Top, Bitmap Bottom) RenderAPage(int pageID, float zoom)
        {
            return RenderPage(ctx, doc, pages[pageID].pagePtr, scale, 2 * zoom);
        }

        ~MuPdf()
        {
            for (int i = 0; i < pages.Count; i++)
            { // 遍历各页
                IntPtr p = pages[i].pagePtr;
                NativeMethods.FreePage(ctx, p); // 释放页面所占用的资源
            }
            NativeMethods.CloseDocument(ctx, doc); // 释放其它资源
            NativeMethods.CloseStream(ctx, stm);
            NativeMethods.FreeContext(ctx);
        }

        /// <summary>
        /// 渲染一页
        /// </summary>
        /// <param name="context"></param>
        /// <param name="document"></param>
        /// <param name="page"></param>
        /// <param name="scale"></param>
        /// <param name="zoom"></param>
        /// <returns></returns>
        static (Bitmap top, Bitmap bottom) RenderPage(IntPtr context, IntPtr document, IntPtr page, double scale, double zoom)
        {
            Matrix ctm = new Matrix();
            IntPtr pix = IntPtr.Zero;
            IntPtr dev = IntPtr.Zero;


            //ctm.A = ctm.D = 1; // 设置单位矩阵 (1,0,0,1,0,0)

            zoom = zoom * scale;

            ctm = NativeMethods.fz_scale((float)zoom, (float)zoom);
            //ctm = NativeMethods.fz_pre_rotate(ctm, 0);
            var c = NativeMethods.FindDeviceColorSpace(context);
            pix = NativeMethods.fz_new_pixmap_from_page(
                context, page, ctm, c, 0);
            int width = NativeMethods.fz_pixmap_width(context, pix);
            int height = NativeMethods.fz_pixmap_height(context, pix);

            // 创建与 Pixmap 相同尺寸的彩色 Bitmap


            var half_h = height / 2;
            Bitmap bmp = new Bitmap(width, half_h, PixelFormat.Format24bppRgb);
            Bitmap bmp2 = new Bitmap(width, height - half_h, PixelFormat.Format24bppRgb);

            var imageData = bmp.LockBits(new System.Drawing.Rectangle(0, 0,
                              width, half_h), ImageLockMode.ReadWrite, bmp.PixelFormat);
            var imageData2 = bmp2.LockBits(new System.Drawing.Rectangle(0, 0,
                            width, height - half_h), ImageLockMode.ReadWrite, bmp.PixelFormat);
            unsafe
            { // 将 Pixmap 的数据转换为 Bitmap 数据
              // 获取  的图像数据
                byte* ptrSrc = (byte*)NativeMethods.GetSamples(context, pix);
                byte* ptrDest = (byte*)imageData.Scan0;
                for (int y = 0; y < half_h; y++)
                {
                    byte* pl = ptrDest;
                    byte* sl = ptrSrc;
                    for (int x = 0; x < width; x++)
                    {
                        // 将 Pixmap 的色彩数据转换为 Bitmap 的格式
                        pl[2] = sl[0]; //b-r
                        pl[1] = sl[1]; //g-g
                        pl[0] = sl[2]; //r-b
                                       //sl[3] 是透明通道数据，在此忽略
                        pl += 3;
                        sl += 3;
                    }
                    ptrDest += imageData.Stride;
                    ptrSrc += width * 3;
                }

                ptrDest = (byte*)imageData2.Scan0;
                for (int y = half_h; y < height; y++)
                {
                    byte* pl = ptrDest;
                    byte* sl = ptrSrc;
                    for (int x = 0; x < width; x++)
                    {
                        // 将 Pixmap 的色彩数据转换为 Bitmap 的格式
                        pl[2] = sl[0]; //b-r
                        pl[1] = sl[1]; //g-g
                        pl[0] = sl[2]; //r-b
                                       //sl[3] 是透明通道数据，在此忽略
                        pl += 3;
                        sl += 3;
                    }
                    ptrDest += imageData.Stride;
                    ptrSrc += width * 3;
                }
            }


            NativeMethods.DropPixmap(context, pix); // 释放 Pixmap 占用的资源
            return (bmp, bmp2);
        }

        /// <summary>
        /// Basic information:
        /// 'format'	-- Document format and version.
        /// 'encryption'	-- Description of the encryption used.
        /// 
        /// From the document information dictionary:
		/// 'info:Title'
		/// 'info:Author'
		/// 'info:Subject'
		/// 'info:Keywords'
		/// 'info:Creator'
		/// 'info:Producer'
		/// 'info:CreationDate'
		/// 'info:ModDate'
        /// </summary>
        /// <param name="key"></param>
        string? lookupMetadate(string key)
        {
            unsafe
            {
                var buffsize = 1024;
                byte* buf = stackalloc byte[buffsize];

                var ak = Encoding.UTF8.GetBytes(key);
                byte* keyp = stackalloc byte[ak.Length + 1];
                for (int i = 0; i < ak.Length; i++)
                {
                    keyp[i] = ak[i];
                }
                keyp[ak.Length] = 0;

                var len = NativeMethods.fz_lookup_metadata(ctx, doc, keyp, buf, buffsize);
                if (len == -1) return null;
                return ReadCStr(buf);
            }
        }
        void BindMetadata()
        {
            Title = lookupMetadate("info:Title");
            Author = lookupMetadate("info:Author");
            Subject = lookupMetadate("info:Subject");
            Keywords = lookupMetadate("info:Keywords");
            Creator = lookupMetadate("info:Creator");
            Producer = lookupMetadate("info:Producer");
            CreationDate = lookupMetadate("info:CreationDate");
            ModDate = lookupMetadate("info:ModDate");
        }

        public string? Title { get; private set; }
        public string? Author { get; private set; }
        public string? Subject { get; private set; }
        public string? Keywords { get; private set; }
        public string? Creator { get; private set; }
        public string? Producer { get; private set; }
        public string? CreationDate { get; private set; }
        public string? ModDate { get; private set; }
    }
   


    public struct BBox
    {
        public int Left, Top, Right, Bottom;
    }
    public struct Rectangle
    {
        public float Left, Top, Right, Bottom;
    }
    public struct Matrix
    {
        public float A, B, C, D, E, F;
    }

    public unsafe struct fz_outline
    {
        public int refs;
        public byte* title;
        public byte* uri;
        public int page;
        public float x, y;
        public fz_outline* next;
        public fz_outline* down;
        public int is_open;
    }
    public class OutlineIndex
    {
        public int refs;
        public string title;
        public string uri;
        public int page;
        public float x, y;
        public List<OutlineIndex>? down;
    }

    class NativeMethods
    {

        const string DLL = "libmupdf.dll";

        [DllImport(DLL, EntryPoint = "fz_new_context_imp", CharSet = CharSet.Ansi)]
        public static extern IntPtr NewContext(IntPtr alloc, IntPtr locks, uint max_store, string version = "1.17.0");

        [DllImport(DLL, EntryPoint = "fz_free_context")]
        public static extern IntPtr FreeContext(IntPtr ctx);

        [DllImport(DLL, EntryPoint = "fz_open_file_w", CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenFile(IntPtr ctx, string fileName);

        [DllImport(DLL, EntryPoint = "fz_open_document_with_stream", CharSet = CharSet.Ansi)]
        public static extern IntPtr OpenDocumentStream(IntPtr ctx, string magic, IntPtr stm);

        [DllImport(DLL, EntryPoint = "fz_close")]
        public static extern IntPtr CloseStream(IntPtr ctx, IntPtr stm);

        [DllImport(DLL, EntryPoint = "fz_close_document")]
        public static extern IntPtr CloseDocument(IntPtr ctx, IntPtr doc);

        [DllImport(DLL, EntryPoint = "fz_count_pages")]
        public static extern int CountPages(IntPtr ctx, IntPtr doc);

        [DllImport(DLL, EntryPoint = "fz_bound_page")]
        public static extern Rectangle BoundPage(IntPtr ctx, IntPtr page);

        [DllImport(DLL, EntryPoint = "fz_clear_pixmap_with_value")]
        public static extern void ClearPixmap(IntPtr ctx, IntPtr pix, int byteValue);

        [DllImport(DLL, EntryPoint = "fz_page_separations")]
        public static extern IntPtr fz_page_separations(IntPtr ctx, IntPtr page);

        [DllImport(DLL, EntryPoint = "fz_device_rgb")]
        public static extern IntPtr FindDeviceColorSpace(IntPtr ctx);

        [DllImport(DLL, EntryPoint = "fz_free_device")]
        public static extern void FreeDevice(IntPtr ctx, IntPtr dev);

        [DllImport(DLL, EntryPoint = "fz_close_device")]
        public static extern void CloseDevice(IntPtr ctx, IntPtr dev);

        [DllImport(DLL, EntryPoint = "fz_free_page")]
        public static extern void FreePage(IntPtr doc, IntPtr page);

        [DllImport(DLL, EntryPoint = "fz_load_page")]
        public static extern IntPtr LoadPage(IntPtr ctx, IntPtr doc, int pageNumber);

        [DllImport(DLL, EntryPoint = "fz_new_draw_device")]
        public static extern IntPtr NewDrawDevice(IntPtr ctx, Matrix ind, IntPtr pix);

        [DllImport(DLL, EntryPoint = "fz_new_pixmap")]
        public static extern IntPtr NewPixmap(IntPtr ctx, IntPtr colorspace, int width, int height, IntPtr seps, int alpha);

        [DllImport(DLL, EntryPoint = "fz_run_page")]
        public static extern void RunPage(IntPtr ctx, IntPtr doc, IntPtr page, IntPtr dev, Matrix transform, IntPtr cookie);

        [DllImport(DLL, EntryPoint = "fz_drop_pixmap")]
        public static extern void DropPixmap(IntPtr ctx, IntPtr pix);

        [DllImport(DLL, EntryPoint = "fz_pixmap_samples")]
        public static extern IntPtr GetSamples(IntPtr ctx, IntPtr pix);

        [DllImport(DLL, EntryPoint = "fz_register_document_handlers")]
        public static extern IntPtr fz_register_document_handlers(IntPtr ctx);

        [DllImport(DLL, EntryPoint = "fz_new_pixmap_from_page")]
        public static extern IntPtr fz_new_pixmap_from_page(IntPtr ctx, IntPtr pag, Matrix ctm, IntPtr colorspace, int aplha);

        [DllImport(DLL, EntryPoint = "fz_scale")]
        public static extern Matrix fz_scale(float a, float b);

        [DllImport(DLL, EntryPoint = "fz_pre_rotate")]
        public static extern Matrix fz_pre_rotate(Matrix a, float degree);

        [DllImport(DLL, EntryPoint = "fz_pixmap_width")]
        public static extern int fz_pixmap_width(IntPtr doc, IntPtr pix);
        [DllImport(DLL, EntryPoint = "fz_pixmap_height")]
        public static extern int fz_pixmap_height(IntPtr doc, IntPtr pix);

        [DllImport(DLL, EntryPoint = "fz_lookup_metadata")]
        public static unsafe extern int fz_lookup_metadata(IntPtr ctx, IntPtr doc, byte* key, byte* buf, int size);

        [DllImport(DLL, EntryPoint = "fz_load_outline")]
        public static unsafe extern fz_outline* fz_load_outline(IntPtr ctx, IntPtr doc);


    }
}
