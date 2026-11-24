// Pacote de dados embutido no .exe (recurso "pacote.bin", montado por ferramentas\empacotar.exe):
//   dados/*.tsv|json, offsets.ini, icone.ico  e  icones.bin = [u16 tamNome][nome][u16 w][u16 h][BGRA]... (ícones de 32 px)
// Sem o recurso (compilação de teste), o analyzer lê as pastas dados\ e icones\ ao lado do .exe.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BloodstoneHunt
{
    static class Pacote
    {
        static readonly Dictionary<string, byte[]> arquivos = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static byte[] icones;
        public static bool Existe { get; private set; }

        static Pacote()
        {
            try
            {
                using (var s = typeof(Pacote).Assembly.GetManifestResourceStream("pacote.bin"))
                {
                    if (s == null) return;
                    using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                        foreach (var e in zip.Entries)
                            using (var es = e.Open())
                            using (var ms = new MemoryStream())
                            {
                                es.CopyTo(ms);
                                if (e.FullName == "icones.bin") icones = ms.ToArray(); else arquivos[e.FullName] = ms.ToArray();
                            }
                }
                if (icones != null)
                    for (int p = 0; p + 2 <= icones.Length;)
                    {
                        int tn = BitConverter.ToUInt16(icones, p); var nome = Encoding.UTF8.GetString(icones, p + 2, tn);
                        int q = p + 2 + tn, w = BitConverter.ToUInt16(icones, q), h = BitConverter.ToUInt16(icones, q + 2);
                        indice[nome] = q; p = q + 4 + w * h * 4;
                    }
                Existe = true;
            }
            catch { Existe = false; }
        }

        /** bytes de um arquivo do pacote (ex.: "dados/itens.tsv") ou null */
        public static byte[] Arquivo(string nome) { byte[] b; return arquivos.TryGetValue(nome, out b) ? b : null; }
        public static string Texto(string nome)
        {
            var b = Arquivo(nome); if (b == null) return null;
            var t = Encoding.UTF8.GetString(b); return t.Length > 0 && t[0] == '﻿' ? t.Substring(1) : t;
        }
        public static IEnumerable<string> Nomes(string prefixo) { foreach (var k in arquivos.Keys) if (k.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) yield return k; }

        public static bool TemIcone(string nome) { return indice.ContainsKey(nome); }
        /** ícone do pacote (ex.: "itens/3243", "criaturas/Infected Bear", "monstros/3205") ou null */
        public static ImageSource Icone(string nome)
        {
            ImageSource img; if (cache.TryGetValue(nome, out img)) return img;
            int q; img = null;
            if (indice.TryGetValue(nome, out q))
            {
                int w = BitConverter.ToUInt16(icones, q), h = BitConverter.ToUInt16(icones, q + 2);
                var px = new byte[w * h * 4]; Buffer.BlockCopy(icones, q + 4, px, 0, px.Length);
                var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4); bmp.Freeze(); img = bmp;
            }
            cache[nome] = img; return img;
        }
    }
}
