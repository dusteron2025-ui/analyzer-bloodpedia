// Analyzer Bloodpedia — relatório da caçada em imagem (1920x1080 e 1080x1920), para usar em vídeos e posts.
// As imagens são desenhadas só quando o botão é clicado (nada roda em segundo plano) e ficam em <pasta do analyzer>\Relatórios.
// Visual limpo, nas cores da Bloodpedia (tema roxo da wiki): superfícies escuras lisas, bordas finas, um único destaque roxo,
// ouro para valores em gp e verde/vermelho só para o balanço.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BloodstoneHunt
{
    class LinhaRelatorio { public string Nome, Cor; public long Qtd, Valor; public ImageSource Icone; public UsoItem Uso; }

    class DadosRelatorio
    {
        public DateTime Quando = DateTime.Now; public TimeSpan Duracao; public int Nivel;
        public long Xp, Loot, Supplies, Dano, Recebido, Cura, MaiorGolpe, MaxDps, Kills, QtdItens;
        public List<LinhaRelatorio> Monstros = new List<LinhaRelatorio>(), Itens = new List<LinhaRelatorio>();
        public ImageSource Logo;
    }

    static class Relatorio
    {
        public const string NomePasta = "Relatórios";
        static readonly CultureInfo BR = CultureInfo.GetCultureInfo("pt-BR");

        // ---------------- cores da wiki (global.css, tema roxo) ----------------
        static readonly Brush Fundo = Cor("#0F0C16"), Sup = Cor("#1A1426"), Sup2 = Cor("#231B31"), Sup3 = Cor("#2D2440"),
            Linha = Cor("#12FFFFFF"), Linha2 = Cor("#1FFFFFFF"),
            Texto = Cor("#F3F3F7"), Texto2 = Cor("#B9B0CC"), Apagado = Cor("#7D7396"),
            Roxo = Cor("#B38CFF"), Roxo2 = Cor("#C9ABFF"), TintaRoxo = Cor("#1A1030"),
            Ouro = Cor("#F5C35B"), Verde = Cor("#3ECF8E"), Perigo = Cor("#FF6B6B"), Laranja = Cor("#FF8A3D"), Azul = Cor("#7AA7FF");
        static readonly FontFamily Ui = new FontFamily("Segoe UI"), Glifo = new FontFamily("Segoe MDL2 Assets");

        static Brush Cor(string h) { var b = new SolidColorBrush(C(h)); b.Freeze(); return b; }
        static Color C(string h) { return (Color)ColorConverter.ConvertFromString(h); }

        public static List<string> Gerar(DadosRelatorio d, string pastaApp)
        {
            var pasta = System.IO.Path.Combine(pastaApp, NomePasta);
            Directory.CreateDirectory(pasta);
            var baseNome = "relatorio_" + d.Quando.ToString("yyyy-MM-dd_HH-mm-ss");
            var arquivos = new List<string>();
            foreach (var paisagem in new[] { true, false })
            {
                int w = paisagem ? 1920 : 1080, h = paisagem ? 1080 : 1920;
                var arq = System.IO.Path.Combine(pasta, baseNome + "_" + w + "x" + h + ".png");
                Salvar(Montar(d, paisagem), w, h, arq);
                arquivos.Add(arq);
            }
            return arquivos;
        }

        static void Salvar(FrameworkElement el, int w, int h, string arq)
        {
            el.Width = w; el.Height = h;
            el.Measure(new Size(w, h)); el.Arrange(new Rect(0, 0, w, h)); el.UpdateLayout();
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = File.Create(arq)) enc.Save(f);
        }

        // ---------------- montagem ----------------
        const double Vao = 24;

        static FrameworkElement Montar(DadosRelatorio d, bool paisagem)
        {
            var raiz = new Grid { Background = Fundo, ClipToBounds = true };
            TextOptions.SetTextFormattingMode(raiz, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(raiz, TextRenderingMode.Grayscale);
            RenderOptions.SetBitmapScalingMode(raiz, BitmapScalingMode.NearestNeighbor);
            // uma luz roxa bem discreta no alto, como o topo da wiki
            var luz = new RadialGradientBrush { Center = new Point(paisagem ? 0.3 : 0.5, 0), GradientOrigin = new Point(paisagem ? 0.3 : 0.5, 0), RadiusX = paisagem ? 0.6 : 0.9, RadiusY = paisagem ? 0.55 : 0.3 };
            luz.GradientStops.Add(new GradientStop(Color.FromArgb(34, 179, 140, 255), 0));
            luz.GradientStops.Add(new GradientStop(Color.FromArgb(0, 179, 140, 255), 1));
            raiz.Children.Add(new Rectangle { Fill = luz });

            var g = new Grid { Margin = paisagem ? new Thickness(56, 44, 56, 36) : new Thickness(48, 34, 48, 30) };
            raiz.Children.Add(g);
            double h = Math.Max(d.Duracao.TotalHours, 1.0 / 60);

            var stats = new UniformGrid { Columns = paisagem ? 4 : 2, Margin = new Thickness(-Vao / 2, 0, -Vao / 2, Vao) };
            stats.Children.Add(Stat("XP ganho", Num(d.Xp), Num(d.Xp / h) + " por hora", Azul, paisagem));
            stats.Children.Add(Stat("Loot", Num(d.Loot) + " gp", Num(d.Loot / h) + " gp por hora", Ouro, paisagem));
            stats.Children.Add(Stat("Supplies", Num(d.Supplies) + " gp", Num(d.Supplies / h) + " gp por hora", Laranja, paisagem));
            stats.Children.Add(Stat("Monstros mortos", Inteiro(d.Kills), (d.Kills / h).ToString("0.#", BR) + " por hora", Roxo, paisagem));

            if (paisagem)
            {
                Linhas(g, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
                Poe(g, Topo(d), 0);
                Poe(g, Destaque(d, h, true), 1);
                Poe(g, stats, 2);
                var meio = new Grid();
                foreach (var w in new[] { new GridLength(430), new GridLength(Vao), new GridLength(560), new GridLength(Vao), new GridLength(1, GridUnitType.Star) })
                    meio.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
                meio.Children.Add(Combate(d, h, false));
                var m = Monstros(d, 5); Grid.SetColumn(m, 2); meio.Children.Add(m);
                var l = Loot(d, 7, 3); Grid.SetColumn(l, 4); meio.Children.Add(l);
                Poe(g, meio, 3);
                Poe(g, Rodape(), 4);
            }
            else
            {
                Linhas(g, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto);
                Poe(g, Topo(d), 0);
                Poe(g, Destaque(d, h, false), 1);
                Poe(g, stats, 2);
                var c = Combate(d, h, true); c.Margin = new Thickness(0, 0, 0, Vao); Poe(g, c, 3);
                var m = Monstros(d, 4); m.Margin = new Thickness(0, 0, 0, Vao); Poe(g, m, 4);
                Poe(g, Loot(d, 8, 3), 5);
                Poe(g, Rodape(), 6);
            }
            return raiz;
        }

        static void Linhas(Grid g, params GridLength[] a) { foreach (var x in a) g.RowDefinitions.Add(new RowDefinition { Height = x }); }
        static void Poe(Grid g, UIElement e, int linha) { Grid.SetRow(e, linha); g.Children.Add(e); }

        // ---------------- peças ----------------
        static TextBlock T(string s, double tam, Brush cor, FontWeight peso)
        {
            return new TextBlock { Text = s, FontSize = tam, Foreground = cor, FontWeight = peso, FontFamily = Ui, TextTrimming = TextTrimming.CharacterEllipsis };
        }
        // rótulo pequeno em caixa alta, como "PRÓXIMOS EVENTOS" na wiki
        static TextBlock Rotulo(string s, double tam = 14)
        {
            return T(s.ToUpper(BR), tam, Apagado, FontWeights.SemiBold);
        }
        static Border Cartao(double pad = 28)
        {
            return new Border { CornerRadius = new CornerRadius(24), Background = Sup, BorderBrush = Linha, BorderThickness = new Thickness(1), Padding = new Thickness(pad, pad - 4, pad, pad - 4) };
        }
        static Border Pilula(string glifo, string texto, Brush cor, Brush fundo = null, bool forte = false)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            if (glifo != null) { var gl = new TextBlock { Text = glifo, FontFamily = Glifo, FontSize = 14, Foreground = cor, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 8, 0) }; s.Children.Add(gl); }
            var t = T(texto, 16, cor, forte ? FontWeights.Bold : FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(t);
            return new Border { CornerRadius = new CornerRadius(100), Padding = new Thickness(16, 7, 18, 8), Child = s, Background = fundo ?? Sup2,
                BorderBrush = fundo == null ? Linha : Brushes.Transparent, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        }
        static Grid TituloSecao(string nome, string direita)
        {
            var cab = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            cab.Children.Add(T(nome, 22, Texto, FontWeights.SemiBold));
            if (direita != null) { var r = T(direita, 16, Apagado, FontWeights.Normal); r.HorizontalAlignment = HorizontalAlignment.Right; r.VerticalAlignment = VerticalAlignment.Center; cab.Children.Add(r); }
            return cab;
        }
        static FrameworkElement Barra(double pc, Brush cor, double alto = 6)
        {
            pc = Math.Max(0, Math.Min(1, pc));
            var b = new Grid { Height = alto };
            b.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pc + 1e-6, GridUnitType.Star) });
            b.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - pc + 1e-6, GridUnitType.Star) });
            var trilho = new Border { CornerRadius = new CornerRadius(alto / 2), Background = Sup3 }; Grid.SetColumnSpan(trilho, 2); b.Children.Add(trilho);
            if (pc > 0.005) b.Children.Add(new Border { CornerRadius = new CornerRadius(alto / 2), Background = cor });
            return b;
        }

        // ---------------- topo: marca à esquerda, informações à direita ----------------
        static FrameworkElement Topo(DadosRelatorio d)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, Vao) };
            var marca = new StackPanel { Orientation = Orientation.Horizontal };
            if (d.Logo != null)
            {
                var img = new Image { Source = d.Logo, Height = 64, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                marca.Children.Add(img);
            }
            var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nm.Children.Add(T("BLOODPEDIA", 28, Texto, FontWeights.Bold));
            var sub = T("HUNT ANALYZER", 12, Apagado, FontWeights.SemiBold); sub.Margin = new Thickness(1, -4, 0, 0); nm.Children.Add(sub);
            marca.Children.Add(nm);
            g.Children.Add(marca);

            var dir = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            dir.Children.Add(Pilula("", d.Quando.ToString("dd MMM yyyy · HH:mm", BR), Texto2));
            if (d.Nivel > 0) dir.Children.Add(Pilula("", "Nível " + d.Nivel, Texto2));
            dir.Children.Add(Pilula(null, "Relatório", TintaRoxo, Roxo, true));
            g.Children.Add(dir);
            return g;
        }

        // ---------------- destaque: título grande + balanço ----------------
        static FrameworkElement Destaque(DadosRelatorio d, double h, bool paisagem)
        {
            var b = Cartao(paisagem ? 34 : 32);
            b.Margin = new Thickness(0, 0, 0, Vao);
            // leve degradê roxo vindo da esquerda, como o banner da wiki
            b.Background = new LinearGradientBrush(new GradientStopCollection { new GradientStop(C("#241A36"), 0), new GradientStop(C("#1A1426"), 0.55) }, 0);
            long bal = d.Loot - d.Supplies; bool pos = bal >= 0;
            var esq = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var eb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            eb.Children.Add(new TextBlock { Text = "", FontFamily = Glifo, FontSize = 15, Foreground = Roxo, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0) });
            eb.Children.Add(Rotulo("Relatório da caçada", 15));
            esq.Children.Add(eb);
            var t = new TextBlock { FontFamily = Ui, FontSize = paisagem ? 62 : 54, FontWeight = FontWeights.Bold, Foreground = Texto };
            t.Inlines.Add(new Run("Caçada de "));
            t.Inlines.Add(new Run(string.Format("{0:00}:{1:00}h", (int)d.Duracao.TotalHours, d.Duracao.Minutes)) { Foreground = Laranja });
            esq.Children.Add(t);
            var linha = string.Join("  ·  ", new[] { Num(d.Xp) + " de XP", Num(d.Loot) + " gp de loot", Inteiro(d.Kills) + " monstros" });
            esq.Children.Add(T(linha, 22, Texto2, FontWeights.Normal));

            var bl = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            bl.Children.Add(Rotulo("Balanço", 15));
            var v = T((pos && bal > 0 ? "+" : "") + Num(bal) + " gp", paisagem ? 60 : 52, pos ? Verde : Perigo, FontWeights.Bold);
            v.Margin = new Thickness(0, 0, 0, 2); bl.Children.Add(v);
            var ph = new TextBlock { FontFamily = Ui, FontSize = 18 };
            ph.Inlines.Add(new Run((pos && bal > 0 ? "+" : "") + Num(bal / h) + " gp") { Foreground = Texto, FontWeight = FontWeights.SemiBold });
            ph.Inlines.Add(new Run(" por hora") { Foreground = Apagado });
            bl.Children.Add(ph);

            var g = new Grid();
            if (paisagem)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(esq);
                var sep = new Border { Width = 1, Background = Linha2, Margin = new Thickness(40, 6, 40, 6) }; Grid.SetColumn(sep, 1); g.Children.Add(sep);
                bl.MinWidth = 380; Grid.SetColumn(bl, 2); g.Children.Add(bl);
            }
            else
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.Children.Add(esq);
                var sep = new Border { Height = 1, Background = Linha2, Margin = new Thickness(0, 18, 0, 14) }; Grid.SetRow(sep, 1); g.Children.Add(sep);
                Grid.SetRow(bl, 2); g.Children.Add(bl);
            }
            b.Child = g;
            return b;
        }

        // número com uma barrinha de cor à esquerda (como "Colaboradores em destaque" na wiki)
        static FrameworkElement Stat(string rotulo, string valor, string sub, Brush cor, bool paisagem)
        {
            var b = Cartao(26); b.Margin = new Thickness(Vao / 2, 0, Vao / 2, paisagem ? 0 : Vao * 0.5);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = cor, Margin = new Thickness(0, 4, 20, 4) });
            var s = new StackPanel(); Grid.SetColumn(s, 1);
            s.Children.Add(Rotulo(rotulo));
            var v = T(valor, 42, Texto, FontWeights.Bold);
            s.Children.Add(new Viewbox { Child = v, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Height = 56 });
            s.Children.Add(T(sub, 17, Texto2, FontWeights.Normal));
            g.Children.Add(s);
            b.Child = g;
            return b;
        }

        // ---------------- combate: lista com separadores (paisagem) ou faixa de 4 colunas (retrato) ----------------
        static FrameworkElement Combate(DadosRelatorio d, double h, bool faixa)
        {
            var itens = new[] {
                new[] { "Dano causado", Num(d.Dano), Num(d.Dano / h) + " por hora" },
                new[] { "Dano recebido", Num(d.Recebido), Num(d.Recebido / h) + " por hora" },
                new[] { "Cura", Num(d.Cura), Num(d.Cura / h) + " por hora" },
                new[] { "Maior golpe", Inteiro(d.MaiorGolpe), "num único ataque" } };
            var cores = new[] { Laranja, Perigo, Verde, Roxo };
            var b = Cartao();
            if (faixa)
            {
                var u = new UniformGrid { Columns = 4 };
                for (int i = 0; i < itens.Length; i++)
                {
                    var s = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 22, 0, 0, 0) };
                    s.Children.Add(Rotulo(itens[i][0], 13));
                    s.Children.Add(T(itens[i][1], 30, cores[i], FontWeights.Bold));
                    s.Children.Add(T(itens[i][2], 15, Apagado, FontWeights.Normal));
                    var cel = new Grid(); cel.Children.Add(s);
                    if (i > 0) cel.Children.Add(new Border { Width = 1, Background = Linha2, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) });
                    u.Children.Add(cel);
                }
                b.Child = u;
                return b;
            }
            var st = new StackPanel();
            st.Children.Add(TituloSecao("Combate", null));
            for (int i = 0; i < itens.Length; i++)
            {
                var lin = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 14, 0, 14) };
                var e = new StackPanel();
                e.Children.Add(T(itens[i][0], 19, Texto, FontWeights.SemiBold));
                e.Children.Add(T(itens[i][2], 15, Apagado, FontWeights.Normal));
                lin.Children.Add(e);
                var v = T(itens[i][1], 26, cores[i], FontWeights.Bold); v.HorizontalAlignment = HorizontalAlignment.Right; v.VerticalAlignment = VerticalAlignment.Center; lin.Children.Add(v);
                st.Children.Add(lin);
                if (i < itens.Length - 1) st.Children.Add(new Border { Height = 1, Background = Linha });
            }
            b.Child = st;
            return b;
        }

        // ---------------- monstros ----------------
        static FrameworkElement Monstros(DadosRelatorio d, int max)
        {
            var b = Cartao();
            var s = new StackPanel();
            s.Children.Add(TituloSecao("Monstros mortos", d.Monstros.Count > 0 ? d.Monstros.Count + (d.Monstros.Count == 1 ? " espécie" : " espécies") : null));
            if (d.Monstros.Count == 0) s.Children.Add(T("Nenhum monstro morto nesta caçada.", 18, Apagado, FontWeights.Normal));
            long total = Math.Max(1, d.Monstros.Sum(m => m.Qtd));
            foreach (var m in d.Monstros.Take(max))
            {
                var lin = new Grid { Margin = new Thickness(0, 0, 0, 16) };
                lin.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); lin.ColumnDefinitions.Add(new ColumnDefinition());
                lin.Children.Add(Avatar(m.Icone, m.Nome, 48));
                var dir = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }; Grid.SetColumn(dir, 1);
                var l = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                l.Children.Add(T(m.Nome, 19, Texto, FontWeights.SemiBold));
                var q = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, FontFamily = Ui };
                q.Inlines.Add(new Run((100.0 * m.Qtd / total).ToString("0", BR) + "%   ") { FontSize = 15, Foreground = Apagado });
                q.Inlines.Add(new Run(Inteiro(m.Qtd)) { FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Texto });
                l.Children.Add(q);
                dir.Children.Add(l);
                dir.Children.Add(Barra((double)m.Qtd / d.Monstros[0].Qtd, Roxo));
                lin.Children.Add(dir);
                s.Children.Add(lin);
            }
            if (d.Monstros.Count > max) s.Children.Add(T("+ " + (d.Monstros.Count - max) + " espécies · " + Inteiro(d.Monstros.Skip(max).Sum(m => m.Qtd)) + " mortes", 15, Apagado, FontWeights.Normal));
            b.Child = s;
            return b;
        }
        static FrameworkElement Avatar(ImageSource img, string nome, double lado)
        {
            var gr = new Grid { Width = lado, Height = lado };
            gr.Children.Add(new Border { CornerRadius = new CornerRadius(lado / 2), Background = Sup2, BorderBrush = Linha2, BorderThickness = new Thickness(1) });
            if (img != null) gr.Children.Add(new Image { Source = img, Width = lado * 0.8, Height = lado * 0.8, Stretch = Stretch.Uniform });
            else { var t = T(nome.Substring(0, 1).ToUpper(BR), lado * 0.4, Roxo2, FontWeights.Bold); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center; gr.Children.Add(t); }
            return gr;
        }

        // ---------------- loot ----------------
        static FrameworkElement Loot(DadosRelatorio d, int colunas, int linhas)
        {
            var b = Cartao();
            var s = new StackPanel();
            var cab = TituloSecao("Loot", null);
            var leg = Legenda(); leg.HorizontalAlignment = HorizontalAlignment.Right; leg.VerticalAlignment = VerticalAlignment.Center; cab.Children.Add(leg);
            s.Children.Add(cab);
            if (d.Itens.Count == 0) s.Children.Add(T("Nenhum item coletado nesta caçada.", 18, Apagado, FontWeights.Normal));
            int max = colunas * linhas; bool sobra = d.Itens.Count > max; int mostrar = sobra ? max - 1 : max;
            var u = new UniformGrid { Columns = colunas };
            foreach (var it in d.Itens.Take(mostrar)) u.Children.Add(Bloco(it));
            if (sobra)
            {
                var resto = d.Itens.Skip(mostrar).ToList();
                var r = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var a = T("+" + resto.Count, 26, Texto, FontWeights.Bold); a.HorizontalAlignment = HorizontalAlignment.Center; r.Children.Add(a);
                var c = T("itens", 14, Apagado, FontWeights.Normal); c.HorizontalAlignment = HorizontalAlignment.Center; r.Children.Add(c);
                var st = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Width = 92, HorizontalAlignment = HorizontalAlignment.Center };
                st.Children.Add(new Border { Width = 92, Height = 92, CornerRadius = new CornerRadius(18), BorderBrush = Linha2, BorderThickness = new Thickness(1), Background = Fundo, Child = r });
                var v = T(Num(resto.Sum(x => x.Valor)) + " gp", 15, Ouro, FontWeights.SemiBold); v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = new Thickness(0, 7, 0, 0); st.Children.Add(v);
                u.Children.Add(st);
            }
            s.Children.Add(u);
            b.Child = s;
            return b;
        }

        static FrameworkElement Bloco(LinhaRelatorio it)
        {
            var s = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Width = 92, HorizontalAlignment = HorizontalAlignment.Center };
            var caixa = new Grid { Width = 92, Height = 92 };
            caixa.Children.Add(new Border { CornerRadius = new CornerRadius(18), Background = Sup2, BorderBrush = Linha, BorderThickness = new Thickness(1) });
            if (it.Icone != null) caixa.Children.Add(new Image { Source = it.Icone, Width = 60, Height = 60 });
            // raridade: só um pontinho da cor do chat de Pilhagem (itens comuns não têm)
            if (it.Cor != null && it.Cor != "#969696")
                caixa.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(C(it.Cor)), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) });
            var q = T(it.Qtd >= 10000 ? Num(it.Qtd) : Inteiro(it.Qtd), 16, Texto, FontWeights.Bold);
            q.HorizontalAlignment = HorizontalAlignment.Right; q.VerticalAlignment = VerticalAlignment.Bottom; q.Margin = new Thickness(0, 0, 9, 5);
            q.Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 1, Color = Colors.Black };
            caixa.Children.Add(q);
            if (it.Uso != null)
            {
                var selos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                if (it.Uso.Diarias.Count > 0) selos.Children.Add(Selo("", "#3D7BEA", 20));
                if (it.Uso.Forja.Count > 0) selos.Children.Add(Selo("", "#E07B24", 20));
                if (it.Uso.Refino.Count > 0) selos.Children.Add(Selo("", "#8A5CE6", 20));
                caixa.Children.Add(selos);
            }
            s.Children.Add(caixa);
            var v = T(it.Valor > 0 ? Num(it.Valor) + " gp" : "—", 15, it.Valor > 0 ? Ouro : Apagado, FontWeights.SemiBold);
            v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = new Thickness(0, 7, 0, 0);
            s.Children.Add(v);
            return s;
        }

        static Border Selo(string glifo, string cor, double lado)
        {
            return new Border { Width = lado, Height = lado, CornerRadius = new CornerRadius(lado / 2), Background = Cor(cor), BorderBrush = Sup2, BorderThickness = new Thickness(2), Margin = new Thickness(0, 0, 2, 0),
                Child = new TextBlock { Text = glifo, FontFamily = Glifo, FontSize = lado * 0.42, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        }

        static StackPanel Legenda()
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var x in new[] { new[] { "", "#3D7BEA", "Diária" }, new[] { "", "#E07B24", "Forja" }, new[] { "", "#8A5CE6", "Refino" } })
            {
                var se = Selo(x[0], x[1], 20); se.BorderBrush = Sup; se.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(se);
                var t = T(x[2], 15, Texto2, FontWeights.Normal); t.Margin = new Thickness(5, 0, 14, 0); t.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(t);
            }
            ((FrameworkElement)s.Children[s.Children.Count - 1]).Margin = new Thickness(5, 0, 0, 0);
            return s;
        }

        static FrameworkElement Rodape()
        {
            var g = new Grid { Margin = new Thickness(4, 16, 4, 0) };
            var e = new TextBlock { FontFamily = Ui, FontSize = 15 };
            e.Inlines.Add(new Run("Gerado com o ") { Foreground = Apagado });
            e.Inlines.Add(new Run(Info.Nome) { Foreground = Texto2, FontWeight = FontWeights.SemiBold });
            e.Inlines.Add(new Run(" v" + Info.Versao) { Foreground = Apagado });
            g.Children.Add(e);
            var d = T("Preços de NPC da Bloodpedia ou definidos pelo jogador", 15, Apagado, FontWeights.Normal); d.HorizontalAlignment = HorizontalAlignment.Right;
            g.Children.Add(d);
            return g;
        }

        static string Num(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e9) return (v / 1e9).ToString(a >= 1e10 ? "0.#" : "0.##", BR) + "kkk";
            if (a >= 1e6) return (v / 1e6).ToString(a >= 1e7 ? "0.#" : "0.##", BR) + "kk";
            if (a >= 1e4) return (v / 1e3).ToString("0.#", BR) + "k";
            return Math.Round(v).ToString("N0", BR);
        }
        static string Inteiro(long v) { return v.ToString("N0", BR); }
    }
}
