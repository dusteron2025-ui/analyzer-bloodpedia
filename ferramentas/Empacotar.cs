// Monta o pacote de dados do Analyzer Bloodpedia (embutido no .exe): dados/*.tsv|json, offsets.ini, icone.ico
// e os ícones reduzidos para no máximo 32 px (a tela mostra no máximo 30 px). GIFs de criatura viram PNG do 1º quadro.
// Uso: empacotar.exe <pasta app> <saida.bin>
using System; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging; using System.IO; using System.IO.Compression; using System.Linq;

class Empacotar
{
    const int MAX = 32;
    // devolve [u16 w][u16 h][w*h*4 bytes BGRA pré-multiplicado não] do ícone reduzido
    static byte[] Reduzir(string arquivo)
    {
        using (var src = Image.FromFile(arquivo))
        {
            if (src.RawFormat.Equals(ImageFormat.Gif)) src.SelectActiveFrame(FrameDimension.Time, 0);
            double s = Math.Min(1.0, (double)MAX / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)Math.Round(src.Width * s)), h = Math.Max(1, (int)Math.Round(src.Height * s));
            using (var b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(b))
            using (var ms = new MemoryStream())
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.SmoothingMode = SmoothingMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, w, h));
                var bd = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new byte[w * h * 4];
                for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, px, y * w * 4, w * 4);
                b.UnlockBits(bd);
                for (int i = 0; i < px.Length; i += 4) if (px[i + 3] == 0) { px[i] = 0; px[i + 1] = 0; px[i + 2] = 0; }   // transparente sem cor: comprime melhor
                var bw = new BinaryWriter(ms); bw.Write((ushort)w); bw.Write((ushort)h); bw.Write(px); bw.Flush(); return ms.ToArray();
            }
        }
    }
    static void Main(string[] a)
    {
        string app = a[0], saida = a[1];
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(saida)));
        int n = 0, fora = 0; long bruto = 0;
        // móveis/decoração (categoria K) maiores que um quadrado de 64 px não caem como loot: ficam de fora,
        // a não ser que tenham preço de NPC (ex.: Caixa)
        var catDe = new System.Collections.Generic.Dictionary<string, string>(); var nomeDe = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var l in File.ReadAllLines(Path.Combine(app, "dados", "itens.tsv"))) { var p = l.Split((char)9); if (p.Length >= 3) { catDe[p[1]] = p[2]; nomeDe[p[1]] = p[0]; } }
        var comPreco = new System.Collections.Generic.HashSet<string>(File.ReadAllLines(Path.Combine(app, "dados", "precos.tsv")).Select(l => l.Split((char)9)[0]));
        using (var fs = File.Create(saida))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            Action<string, byte[], CompressionLevel> poe = (nome, bytes, nivel) => { var e = zip.CreateEntry(nome, nivel); using (var s = e.Open()) s.Write(bytes, 0, bytes.Length); n++; };
            foreach (var f in Directory.GetFiles(Path.Combine(app, "dados")))
            { var b = File.ReadAllBytes(f); bruto += b.Length; poe("dados/" + Path.GetFileName(f), b, CompressionLevel.Optimal); }
            foreach (var extra in new[] { "offsets.ini", "icone.ico" })
            { var f = Path.Combine(app, extra); if (File.Exists(f)) { var b = File.ReadAllBytes(f); bruto += b.Length; poe(extra, b, CompressionLevel.Optimal); } }
            // todos os ícones num bloco só (icones.bin): [u16 tamNome][nome UTF-8][u16 w][u16 h][BGRA]... — comprimido junto
            var bloco = new MemoryStream(); var bwb = new BinaryWriter(bloco);
            foreach (var pasta in new[] { "itens", "monstros", "criaturas" })
            {
                var dir = Path.Combine(app, "icones", pasta); if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir).Where(x => x.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)))
                {
                    bruto += new FileInfo(f).Length;
                    if (pasta == "itens")
                    {
                        var id = Path.GetFileNameWithoutExtension(f); string cat, nome;
                        if (catDe.TryGetValue(id, out cat) && cat == "K" && !(nomeDe.TryGetValue(id, out nome) && comPreco.Contains(nome)))
                            using (var im = Image.FromFile(f)) if (im.Width > 64 || im.Height > 64) { fora++; continue; }
                    }
                    byte[] b; try { b = Reduzir(f); } catch { continue; }
                    var nm = System.Text.Encoding.UTF8.GetBytes(pasta + "/" + Path.GetFileNameWithoutExtension(f));
                    bwb.Write((ushort)nm.Length); bwb.Write(nm); bwb.Write(b); n++;
                }
            }
            bwb.Flush(); poe("icones.bin", bloco.ToArray(), CompressionLevel.Optimal); n--;
        }
        Console.WriteLine(string.Format("pacote: {0} arquivos ({3} móveis grandes fora), {1:0.0} MB originais -> {2:0.0} MB", n, bruto / 1048576.0, new FileInfo(saida).Length / 1048576.0, fora));
    }
}
