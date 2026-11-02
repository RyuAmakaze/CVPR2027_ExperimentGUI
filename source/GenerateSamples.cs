using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

// Original, deterministic geometry artwork for checking the experiment UI.
// C# 5 / .NET Framework 4, no third-party packages.
internal static class GenerateSamples
{
    private static readonly string[] Shapes = { "mug", "chair", "lamp", "vase", "table", "teapot", "blocks", "stool" };
    private static readonly Color[] Palette = {
        Color.FromArgb(78, 119, 144), Color.FromArgb(178, 105, 75),
        Color.FromArgb(167, 143, 74), Color.FromArgb(91, 132, 112),
        Color.FromArgb(146, 105, 93), Color.FromArgb(132, 111, 151),
        Color.FromArgb(73, 129, 137), Color.FromArgb(151, 116, 79)
    };

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1 || args.Length > 2)
                throw new ArgumentException("Usage: GenerateSamples.exe <output-data-folder> [contact-sheet.png]");
            string output = Path.GetFullPath(args[0]);
            string contact = args.Length == 2 ? Path.GetFullPath(args[1]) : null;
            if (contact != null && string.Equals(Path.GetDirectoryName(contact).TrimEnd(Path.DirectorySeparatorChar), output.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The contact sheet must be outside the data folder.");
            Directory.CreateDirectory(output);
            if (Directory.GetFiles(output).Any(p => string.Equals(Path.GetExtension(p), ".png", StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Refusing to overwrite a data folder that already contains PNG images.");
            if (contact != null && File.Exists(contact))
                throw new IOException("Refusing to overwrite an existing contact sheet.");
            for (int trial = 0; trial < Shapes.Length; trial++)
                for (int variant = 0; variant < 3; variant++)
                {
                    using (Bitmap image = DrawImage(trial, variant))
                    {
                        string file = string.Format("{0}_{1}_sample_{2}_variant{3}.png", trial + 1, (char)('A' + variant), Shapes[trial], variant + 1);
                        image.Save(Path.Combine(output, file), ImageFormat.Png);
                    }
                }
            if (contact != null) WriteContactSheet(output, contact);
            foreach (string p in Directory.GetFiles(output, "*.png"))
                using (Image i = Image.FromFile(p))
                    if (i.Width != 1024 || i.Height != 1024) throw new InvalidDataException("Incorrect image size: " + p);
            Console.WriteLine("Created and reopened 24 RGB PNG images, each 1024 x 1024, in: " + output);
            if (contact != null) Console.WriteLine("Contact sheet: " + contact);
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }

    private static Bitmap DrawImage(int trial, int variant)
    {
        Bitmap b = new Bitmap(1024, 1024, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(242, 243, 241));
            for (int i = 20; i > 0; i--)
                using (Brush shadow = new SolidBrush(Color.FromArgb(2, 40, 43, 43)))
                    g.FillEllipse(shadow, 239 - i * 2, 768 - i, 546 + i * 4, 65 + i * 2);
            Color c = Palette[trial];
            switch (trial)
            {
                case 0: DrawMug(g, variant, c); break;
                case 1: DrawChair(g, variant, c); break;
                case 2: DrawLamp(g, variant, c); break;
                case 3: DrawVase(g, variant, c); break;
                case 4: DrawTable(g, variant, c); break;
                case 5: DrawTeapot(g, variant, c); break;
                case 6: DrawBlocks(g, variant, c); break;
                case 7: DrawStool(g, variant, c); break;
            }
        }
        return b;
    }

    private static Color Light(Color c, float x)
    {
        return Color.FromArgb((int)(c.R + (255 - c.R) * x), (int)(c.G + (255 - c.G) * x), (int)(c.B + (255 - c.B) * x));
    }
    private static Color Dark(Color c, float x)
    { return Color.FromArgb((int)(c.R * x), (int)(c.G * x), (int)(c.B * x)); }
    private static void Polygon(Graphics g, Color c, params PointF[] points)
    { using (Brush b = new SolidBrush(c)) g.FillPolygon(b, points); }
    private static void Rect(Graphics g, Color c, float x, float y, float w, float h)
    { using (Brush b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h); }
    private static void Ellipse(Graphics g, Color c, float x, float y, float w, float h)
    { using (Brush b = new SolidBrush(c)) g.FillEllipse(b, x, y, w, h); }
    private static void Gradient(Graphics g, RectangleF r, Color left, Color right, GraphicsPath path)
    { using (Brush b = new LinearGradientBrush(r, left, right, 0f)) g.FillPath(b, path); }
    private static void Line(Graphics g, Color c, float w, float x1, float y1, float x2, float y2)
    { using (Pen p = new Pen(c, w)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, x1, y1, x2, y2); } }

    private static void DrawMug(Graphics g, int v, Color c)
    {
        float top = 330 - v * 22, bottom = 746, width = 330 + v * 18, x = 310 - v * 9;
        using (Pen p = new Pen(Dark(c, .82f), 54))
            g.DrawEllipse(p, x + width - 28, top + 74, 160 + v * 12, 225);
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddBezier(x, top, x + 10, bottom + 15, x + 8, bottom, x + width / 2, bottom + 30);
            p.AddBezier(x + width / 2, bottom + 30, x + width - 8, bottom, x + width - 10, bottom + 15, x + width, top);
            p.CloseFigure();
            Gradient(g, new RectangleF(x, top, width, bottom - top + 30), Light(c, .30f), Dark(c, .76f), p);
        }
        Ellipse(g, Light(c, .40f), x, top - 36, width, 80);
        Ellipse(g, Dark(c, .39f), x + 19, top - 21, width - 38, 51);
        if (v == 1) Rect(g, Light(c, .40f), x + 10, top + 162, width - 20, 32);
        if (v == 2) for (int i = 0; i < 3; i++) Line(g, Light(c, .28f), 15, x + 70 + i * 72, top + 74, x + 76 + i * 69, bottom - 45);
    }

    private static void DrawChair(Graphics g, int v, Color c)
    {
        Line(g, Dark(c, .60f), 31, 661, 552, 699, 788);
        Line(g, Dark(c, .60f), 31, 430, 540, 399, 749);
        Line(g, Dark(c, .81f), 34, 347, 615, 316, 819);
        Line(g, Dark(c, .81f), 34, 593, 645, 608, 829);
        Rect(g, Dark(c, .68f), 354, 254, 30, 354);
        Rect(g, Dark(c, .68f), 653, 233, 30, 329);
        if (v == 0)
            Polygon(g, c, new PointF(351, 268), new PointF(677, 246), new PointF(677, 474), new PointF(351, 497));
        else if (v == 1)
            for (int i = 0; i < 4; i++)
                Polygon(g, Light(c, .08f), new PointF(390 + i * 68, 266), new PointF(422 + i * 68, 264), new PointF(422 + i * 68, 479), new PointF(390 + i * 68, 481));
        else
        {
            Polygon(g, c, new PointF(351, 268), new PointF(677, 246), new PointF(677, 325), new PointF(351, 348));
            Polygon(g, c, new PointF(351, 418), new PointF(677, 396), new PointF(677, 474), new PointF(351, 497));
        }
        Polygon(g, Dark(c, .70f), new PointF(320, 597), new PointF(589, 630), new PointF(714, 555), new PointF(714, 589), new PointF(589, 663), new PointF(320, 631));
        Polygon(g, Light(c, .24f), new PointF(320, 597), new PointF(446, 530), new PointF(714, 555), new PointF(589, 630));
    }

    private static void DrawLamp(Graphics g, int v, Color c)
    {
        Ellipse(g, Dark(c, .70f), 346, 730, 332, 62);
        Ellipse(g, Light(c, .15f), 346, 713, 332, 60);
        Line(g, Dark(c, .66f), 24, 512, 720, 512, 341);
        if (v == 1) { Line(g, Dark(c, .66f), 20, 512, 489, 607, 367); Line(g, Dark(c, .66f), 20, 607, 367, 535, 290); }
        int spread = v == 2 ? 260 : 215;
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new[] { new PointF(512 - spread * .60f, 255), new PointF(512 + spread * .60f, 255), new PointF(512 + spread, 475), new PointF(512 - spread, 475) });
            Gradient(g, new RectangleF(512 - spread, 255, spread * 2, 230), Light(c, .52f), c, p);
        }
        Ellipse(g, Light(c, .68f), 512 - spread * .60f, 228, spread * 1.2f, 55);
        Ellipse(g, Dark(c, .71f), 512 - spread, 445, spread * 2, 67);
        Ellipse(g, Light(c, .75f), 512 - spread + 18, 451, spread * 2 - 36, 42);
        if (v == 2) Line(g, Light(c, .65f), 13, 386, 316, 354, 420);
    }

    private static void DrawVase(Graphics g, int v, Color c)
    {
        float narrow = 85 + v * 22, wide = 195 - v * 13;
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddBezier(512 - narrow, 244, 512 - narrow * .8f, 415, 512 - wide * 1.55f, 451, 512 - wide, 663);
            p.AddBezier(512 - wide, 663, 512 - wide * .8f, 802, 512 + wide * .8f, 802, 512 + wide, 663);
            p.AddBezier(512 + wide, 663, 512 + wide * 1.55f, 451, 512 + narrow * .8f, 415, 512 + narrow, 244);
            p.CloseFigure();
            Gradient(g, new RectangleF(512 - wide * 1.25f, 244, wide * 2.5f, 540), Light(c, .37f), Dark(c, .72f), p);
        }
        Ellipse(g, Light(c, .40f), 512 - narrow, 220, narrow * 2, 48);
        Ellipse(g, Dark(c, .40f), 512 - narrow + 16, 230, narrow * 2 - 32, 28);
        if (v == 1) Line(g, Light(c, .32f), 17, 387, 570, 401, 660);
        if (v == 2) { Line(g, Light(c, .28f), 12, 406, 525, 407, 656); Line(g, Light(c, .28f), 12, 450, 518, 451, 692); }
    }

    private static void DrawTable(Graphics g, int v, Color c)
    {
        Line(g, Dark(c, .59f), 33, 628, 468, 658, 740);
        Line(g, Dark(c, .59f), 33, 376, 484, 344, 756);
        Line(g, Dark(c, .83f), 38, 314, 550, 285, 816);
        Line(g, Dark(c, .83f), 38, 578, 604, 588, 831);
        Polygon(g, Dark(c, .65f), new PointF(247, 513), new PointF(594, 576), new PointF(777, 468), new PointF(777, 506), new PointF(594, 614), new PointF(247, 551));
        Polygon(g, Light(c, .28f), new PointF(247, 513), new PointF(430, 405), new PointF(777, 468), new PointF(594, 576));
        if (v == 1) Rect(g, Dark(c, .68f), 392, 576, 24, 151);
        if (v == 2)
        {
            Line(g, Dark(c, .73f), 22, 300, 735, 583, 787);
            Line(g, Dark(c, .68f), 20, 583, 787, 654, 686);
        }
    }

    private static void DrawTeapot(Graphics g, int v, Color c)
    {
        using (Pen p = new Pen(Dark(c, .79f), 52)) g.DrawEllipse(p, 590, 420, 180, 218);
        Polygon(g, Dark(c, .89f), new PointF(369, 488), new PointF(297, 446), new PointF(211, 349), new PointF(233, 494), new PointF(362, 637));
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(320, 397, 376, 343);
            Gradient(g, new RectangleF(320, 397, 376, 343), Light(c, .34f), Dark(c, .76f), p);
        }
        Ellipse(g, Dark(c, .72f), 382, 382, 252, 81);
        Ellipse(g, Light(c, .31f), 382, 360, 252, 76);
        Ellipse(g, Dark(c, .85f), 475, 315, 66, 64);
        Ellipse(g, Light(c, .44f), 479, 311, 58, 34);
        if (v == 1) Ellipse(g, Light(c, .52f), 410, 491, 87, 126);
        if (v == 2) for (int i = 0; i < 3; i++) Line(g, Light(c, .30f), 13, 401 + i * 65, 482, 407 + i * 65, 671);
    }

    private static void Block(Graphics g, float x, float y, float w, float h, Color c)
    {
        float d = w * .32f;
        Polygon(g, c, new PointF(x, y), new PointF(x + w, y + d * .30f), new PointF(x + w, y + h + d * .30f), new PointF(x, y + h));
        Polygon(g, Dark(c, .69f), new PointF(x + w, y + d * .30f), new PointF(x + w + d, y - d * .22f), new PointF(x + w + d, y + h - d * .22f), new PointF(x + w, y + h + d * .30f));
        Polygon(g, Light(c, .32f), new PointF(x, y), new PointF(x + d, y - d * .52f), new PointF(x + w + d, y - d * .22f), new PointF(x + w, y + d * .30f));
    }
    private static void DrawBlocks(Graphics g, int v, Color c)
    {
        Block(g, 279, 586, 345, 167, c);
        Block(g, v == 1 ? 390 : 321, 422, 242, 166, Light(c, .17f));
        Block(g, v == 2 ? 453 : 362, 292, 153, 132, Light(c, .33f));
    }

    private static void DrawStool(Graphics g, int v, Color c)
    {
        Line(g, Dark(c, .62f), 35, 619, 483, 679, 775);
        Line(g, Dark(c, .62f), 35, 422, 477, 379, 770);
        Line(g, Dark(c, .83f), 39, 363, 545, 318, 824);
        Line(g, Dark(c, .83f), 39, 579, 566, 612, 833);
        if (v == 1) { Line(g, Dark(c, .73f), 22, 345, 689, 594, 712); Line(g, Dark(c, .62f), 22, 594, 712, 648, 635); }
        if (v == 2) { Line(g, Dark(c, .73f), 24, 339, 706, 602, 760); Line(g, Dark(c, .65f), 24, 592, 695, 351, 769); }
        Ellipse(g, Dark(c, .71f), 302, 442, 405, 169);
        Rect(g, Dark(c, .71f), 302, 482, 405, 45);
        Ellipse(g, Light(c, .25f), 302, 405, 405, 169);
    }

    private static void WriteContactSheet(string output, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        using (Bitmap sheet = new Bitmap(1080, 8 * 372 + 60, PixelFormat.Format24bppRgb))
        using (Graphics g = Graphics.FromImage(sheet))
        using (Font font = new Font("Segoe UI", 13))
        using (Font title = new Font("Segoe UI", 20, FontStyle.Bold))
        using (Brush ink = new SolidBrush(Color.FromArgb(40, 46, 50)))
        {
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawString("Experiment UI / 1024 x 1024 sample stimuli", title, ink, 20, 13);
            for (int trial = 0; trial < Shapes.Length; trial++)
                for (int v = 0; v < 3; v++)
                {
                    string name = string.Format("{0}_{1}_sample_{2}_variant{3}.png", trial + 1, (char)('A' + v), Shapes[trial], v + 1);
                    using (Image i = Image.FromFile(Path.Combine(output, name)))
                        g.DrawImage(i, 16 + v * 356, 63 + trial * 372, 336, 336);
                    g.DrawString(string.Format("{0}  /  {1}  /  {2}", trial + 1, (char)('A' + v), Shapes[trial]), font, ink, 18 + v * 356, 402 + trial * 372);
                }
            sheet.Save(file, ImageFormat.Png);
        }
    }
}
