// Ícone do Analyzer Bloodpedia: logo da Bloodpedia (quadrado arredondado) + selo com gráfico subindo.
// Uso: iconebp.exe <logo.png> <pastaSaida>  -> icone.ico (16..256) e icone.png (512)
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using System.IO;

class IconeBloodpedia
{
    static bool semSelo;
    static Bitmap Desenhar(Image logo, int S)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(logo, new Rectangle(0, 0, S, S));
            if (semSelo) return bmp;
            // selo: círculo com gradiente laranja -> roxo (cores do logo) e um gráfico de linha subindo
            float r = S * 0.145f, cx = S * 0.80f, cy = S * 0.80f;
            var c = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
            using (var aro = new SolidBrush(Color.FromArgb(255, 24, 16, 32))) g.FillEllipse(aro, RectangleF.Inflate(c, S * 0.025f, S * 0.025f));
            using (var b = new LinearGradientBrush(c, Color.FromArgb(255, 255, 150, 60), Color.FromArgb(255, 170, 70, 220), 45f)) g.FillEllipse(b, c);
            float w = Math.Max(1.3f, S * 0.035f);
            using (var p = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                // barras pequenas ao fundo (só em tamanhos maiores) e a linha subindo com seta
                if (S >= 48)
                    using (var bar = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                    {
                        float bw = r * 0.22f, base0 = cy + r * 0.55f;
                        float[] hs = { 0.35f, 0.6f, 0.9f };
                        for (int i = 0; i < 3; i++) g.FillRectangle(bar, cx - r * 0.55f + i * r * 0.4f, base0 - r * hs[i], bw, r * hs[i]);
                    }
                var pts = new[] { new PointF(cx - r * 0.6f, cy + r * 0.3f), new PointF(cx - r * 0.15f, cy - r * 0.1f), new PointF(cx + r * 0.1f, cy + r * 0.12f), new PointF(cx + r * 0.58f, cy - r * 0.4f) };
                g.DrawLines(p, pts);
                g.DrawLines(p, new[] { new PointF(cx + r * 0.2f, cy - r * 0.44f), new PointF(cx + r * 0.58f, cy - r * 0.4f), new PointF(cx + r * 0.55f, cy - r * 0.02f) });
            }
        }
        return bmp;
    }
    static void Main(string[] a)
    {
        var logo = Image.FromFile(a[0]); string dir = a[1]; semSelo = a.Length > 2 && a[2] == "--sem-selo";
        int[] tam = { 16, 24, 32, 48, 64, 128, 256 }; var pngs = new List<byte[]>();
        foreach (int s in tam) using (var b = Desenhar(logo, s)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
        using (var b = Desenhar(logo, 512)) b.Save(Path.Combine(dir, "icone.png"), ImageFormat.Png);
        using (var f = new BinaryWriter(File.Create(Path.Combine(dir, "icone.ico"))))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)tam.Length); int off = 6 + 16 * tam.Length;
            for (int i = 0; i < tam.Length; i++) { f.Write((byte)(tam[i] >= 256 ? 0 : tam[i])); f.Write((byte)(tam[i] >= 256 ? 0 : tam[i])); f.Write((byte)0); f.Write((byte)0); f.Write((short)1); f.Write((short)32); f.Write(pngs[i].Length); f.Write(off); off += pngs[i].Length; }
            foreach (var p in pngs) f.Write(p);
        }
        Console.WriteLine("ok");
    }
}
