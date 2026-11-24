// Desenha o ícone do analyzer (quadrado arredondado escuro, gota de sangue em gradiente, selo dourado com seta)
// e grava icone.ico (16..256) e icone.png. Uso: icone.exe <pastaSaida>
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using System.IO;

class Icone
{
    static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var p = new GraphicsPath(); float d = raio * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    // gota: ponta em cima, corpo redondo embaixo (coordenadas numa caixa 0..1)
    static GraphicsPath Gota(float x, float y, float w, float h)
    {
        var p = new GraphicsPath();
        Func<float, float, PointF> P = (a, b) => new PointF(x + a * w, y + b * h);
        p.AddBezier(P(0.5f, 0f), P(0.62f, 0.2f), P(1f, 0.45f), P(1f, 0.66f));
        p.AddBezier(P(1f, 0.66f), P(1f, 0.86f), P(0.78f, 1f), P(0.5f, 1f));
        p.AddBezier(P(0.5f, 1f), P(0.22f, 1f), P(0f, 0.86f), P(0f, 0.66f));
        p.AddBezier(P(0f, 0.66f), P(0f, 0.45f), P(0.38f, 0.2f), P(0.5f, 0f));
        p.CloseFigure(); return p;
    }

    static Bitmap Desenhar(int S)
    {
        var bmp = new Bitmap(S, S, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.Clear(Color.Transparent);
            float m = S * 0.02f; var caixa = new RectangleF(m, m, S - 2 * m, S - 2 * m); float raio = S * 0.23f;
            using (var fundo = Arredondado(caixa, raio))
            {
                using (var b = new LinearGradientBrush(caixa, Color.FromArgb(255, 44, 48, 57), Color.FromArgb(255, 20, 22, 27), 90f)) g.FillPath(b, fundo);
                if (S >= 32) using (var c = new Pen(Color.FromArgb(255, 58, 63, 73), Math.Max(1f, S * 0.012f))) g.DrawPath(c, fundo);
            }
            // brilho vermelho atrás da gota
            if (S >= 32)
            {
                var halo = new GraphicsPath(); halo.AddEllipse(S * 0.14f, S * 0.18f, S * 0.66f, S * 0.66f);
                using (var hb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(90, 229, 72, 77), SurroundColors = new[] { Color.FromArgb(0, 229, 72, 77) } }) g.FillPath(hb, halo);
            }
            // gota
            float gw = S * 0.46f, gh = S * 0.60f, gx = S * 0.24f, gy = S * 0.14f;
            using (var gota = Gota(gx, gy, gw, gh))
            {
                using (var b = new LinearGradientBrush(new RectangleF(gx, gy, gw, gh), Color.FromArgb(255, 255, 112, 112), Color.FromArgb(255, 178, 34, 58), 70f)) g.FillPath(b, gota);
                if (S >= 24) using (var c = new Pen(Color.FromArgb(120, 255, 190, 190), Math.Max(1f, S * 0.01f))) g.DrawPath(c, gota);
            }
            // reflexo
            if (S >= 24)
                using (var r = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                    g.FillEllipse(r, gx + gw * 0.2f, gy + gh * 0.52f, gw * 0.16f, gh * 0.22f);
            // selo dourado com seta para cima (lucro)
            float sr = S * 0.20f, sx = S * 0.70f, sy = S * 0.70f;
            var selo = new RectangleF(sx - sr, sy - sr, sr * 2, sr * 2);
            using (var anel = new SolidBrush(Color.FromArgb(255, 22, 24, 29))) g.FillEllipse(anel, RectangleF.Inflate(selo, S * 0.035f, S * 0.035f));
            using (var b = new LinearGradientBrush(selo, Color.FromArgb(255, 255, 226, 154), Color.FromArgb(255, 201, 130, 30), 60f)) g.FillEllipse(b, selo);
            using (var seta = new Pen(Color.FromArgb(255, 40, 30, 12), Math.Max(1.2f, S * 0.04f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                var pts = new[] { new PointF(sx - sr * 0.55f, sy + sr * 0.35f), new PointF(sx - sr * 0.12f, sy - sr * 0.05f), new PointF(sx + sr * 0.12f, sy + sr * 0.18f), new PointF(sx + sr * 0.55f, sy - sr * 0.35f) };
                g.DrawLines(seta, pts);
                g.DrawLines(seta, new[] { new PointF(sx + sr * 0.18f, sy - sr * 0.38f), new PointF(sx + sr * 0.55f, sy - sr * 0.35f), new PointF(sx + sr * 0.52f, sy + sr * 0.02f) });
            }
        }
        return bmp;
    }

    static void Main(string[] a)
    {
        string dir = a.Length > 0 ? a[0] : ".";
        int[] tam = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in tam) using (var b = Desenhar(s)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
        using (var b = Desenhar(512)) b.Save(Path.Combine(dir, "icone.png"), ImageFormat.Png);
        using (var f = new BinaryWriter(File.Create(Path.Combine(dir, "icone.ico"))))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)tam.Length);
            int off = 6 + 16 * tam.Length;
            for (int i = 0; i < tam.Length; i++)
            {
                f.Write((byte)(tam[i] >= 256 ? 0 : tam[i])); f.Write((byte)(tam[i] >= 256 ? 0 : tam[i])); f.Write((byte)0); f.Write((byte)0);
                f.Write((short)1); f.Write((short)32); f.Write(pngs[i].Length); f.Write(off); off += pngs[i].Length;
            }
            foreach (var p in pngs) f.Write(p);
        }
        Console.WriteLine("icone.ico e icone.png gravados em " + dir);
    }
}
