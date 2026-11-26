// Analyzer Bloodpedia — acessibilidade: idioma, tamanho do texto, alto contraste e fundo da janela.
//
// Em vez de mexer em cada janela, um "ouvinte" global pega todo elemento quando ele aparece na tela (evento Loaded) e
// aplica as opções: traduz textos fixos (pelo dicionário idiomas.tsv: português → inglês, espanhol), aumenta a fonte,
// clareia textos e bordas no alto contraste e deixa o fundo translúcido. Vale também para o que é criado depois
// (Party Hunt, avisos). Ao mudar uma opção, Reaplicar() passa de novo por todas as janelas abertas.
// O original de cada elemento fica guardado; se o código mudar um texto ou uma cor depois, isso vira o novo original.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace BloodstoneHunt
{
    static class Acess
    {
        public static string Idioma = "pt";          // pt, en, es
        public static double FatorTexto = 1;         // 1, 1.15, 1.3
        public static bool Contraste;
        public static double Fundo = 1;              // opacidade do fundo das janelas: 1, 0.8, 0.6
        static bool mexeu;                           // alguma opção já saiu do padrão nesta sessão (para conseguir voltar)
        static bool Padrao { get { return Idioma == "pt" && FatorTexto == 1 && !Contraste && Fundo == 1; } }

        // fonte de cada janela: cresce um pouco quando a pessoa deixa a janela mais larga (1 = normal)
        public static readonly DependencyProperty FatorJanelaProperty = DependencyProperty.RegisterAttached("FatorJanela", typeof(double), typeof(Acess), new PropertyMetadata(1.0));
        public static double FatorJanela(Window w) { return (double)w.GetValue(FatorJanelaProperty); }
        public static void DefinirFatorJanela(Window w, double f)
        {
            if (Math.Abs((double)w.GetValue(FatorJanelaProperty) - f) < 0.001) return;
            w.SetValue(FatorJanelaProperty, f); mexeu = true; Percorrer(w);
        }
        static double fatorAtual = 1;   // tamanho do texto × fonte da janela que está sendo percorrida

        // ---------------- idioma ----------------
        static Dictionary<string, string[]> dic;
        static void CarregarDicionario()
        {
            dic = new Dictionary<string, string[]>();
            try
            {
                using (var r = typeof(Acess).Assembly.GetManifestResourceStream("idiomas.tsv"))
                using (var sr = new StreamReader(r, Encoding.UTF8))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                    {
                        if (l.Length == 0 || l.StartsWith("#")) continue;
                        var p = l.Split('\t'); if (p.Length < 3) continue;
                        dic[p[0]] = new[] { p[1], p[2] };
                    }
                }
            }
            catch { }
        }
        /** traduz um texto fixo (em português); sem tradução, fica como está */
        public static string T(string pt)
        {
            if (pt == null || Idioma == "pt") return pt;
            if (dic == null) CarregarDicionario();
            string[] t; if (!dic.TryGetValue(pt, out t)) return pt;
            var x = Idioma == "en" ? t[0] : t[1];
            return string.IsNullOrEmpty(x) ? pt : x;
        }

        // ---------------- alto contraste ----------------
        static readonly Dictionary<Color, Color> MapaTexto = Mapa(
            "#626772", "#B8BDC6", "#7D828C", "#C6CBD3", "#8B909A", "#CFD3DA", "#9AA0AA", "#DADEE4", "#C9CDD4", "#F2F4F7",
            "#E9EBEE", "#FFFFFF", "#555A64", "#AEB3BC", "#5A606B", "#AEB3BC", "#7D7396", "#C9C2DE", "#B9B0CC", "#E6E0F2");
        static readonly Dictionary<Color, Color> MapaFundo = Mapa("#17191E", "#07080A", "#22252C", "#111317", "#1E2128", "#0E1013", "#101216", "#000000");
        static readonly Dictionary<Color, Color> MapaBorda = Mapa("#30343C", "#8A909C", "#2A2E35", "#6A707C", "#3A3F49", "#8A909C", "#4A4128", "#A08A4A");
        static Dictionary<Color, Color> Mapa(params string[] pares)
        {
            var d = new Dictionary<Color, Color>();
            for (int i = 0; i + 1 < pares.Length; i += 2) d[(Color)ColorConverter.ConvertFromString(pares[i])] = (Color)ColorConverter.ConvertFromString(pares[i + 1]);
            return d;
        }
        static Brush Trocar(Brush b, Dictionary<Color, Color> mapa, double alfa = 1)
        {
            var s = b as SolidColorBrush; if (s == null) return b;
            var c = s.Color; Color n;
            if (Contraste && mapa.TryGetValue(Color.FromRgb(c.R, c.G, c.B), out n)) c = Color.FromArgb(c.A, n.R, n.G, n.B);
            if (alfa < 1) c = Color.FromArgb((byte)(c.A * alfa), c.R, c.G, c.B);
            if (c == s.Color) return b;
            var r = new SolidColorBrush(c); r.Freeze(); return r;
        }

        // ---------------- aplicar ----------------
        class Orig { public string Texto, TextoAplicado; public double Fonte, FonteAplicada; public Brush Cor, CorAplicada, Fundo, FundoAplicado, Borda, BordaAplicada; public object Conteudo, ConteudoAplicado, Dica, DicaAplicada; public double Altura, AlturaAplicada, Largura, LarguraAplicada; }
        static readonly ConditionalWeakTable<object, Orig> origs = new ConditionalWeakTable<object, Orig>();

        public static void Ligar()
        {
            // ao abrir cada janela, ajusta tudo o que está nela
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, e) => { if (!Padrao || mexeu) Percorrer(s as DependencyObject); }), true);
            // e, a cada meio segundo, o que apareceu depois (itens novos do loot, cartões do grupo…); só passa se não estiver no padrão
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            t.Tick += (s, e) => { if (Padrao && !mexeu) return; foreach (Window w in Application.Current.Windows) if (w.IsVisible) Percorrer(w); };
            t.Start();
        }
        /** ajusta na hora um pedaço que acabou de ser montado (evita mostrar o original por meio segundo) */
        public static void AplicarEm(DependencyObject d) { if (!Padrao || mexeu) Percorrer(d); }

        /** aplica as opções num elemento (e nos trechos de texto dele) */
        public static void Aplicar(DependencyObject d)
        {
            if (d == null || (Padrao && !mexeu)) return;
            var o = origs.GetValue(d, k => new Orig());
            var fe = d as FrameworkElement;

            // dica (ToolTip) de texto
            if (fe != null && (fe.ToolTip is string || o.Dica != null))
            {
                if (o.Dica == null || !Equals(fe.ToolTip, o.DicaAplicada)) o.Dica = fe.ToolTip;
                if (o.Dica is string) { o.DicaAplicada = T((string)o.Dica); fe.ToolTip = o.DicaAplicada; }
            }

            var tb = d as TextBlock;
            if (tb != null)
            {
                if (!BindingOperations.IsDataBound(tb, TextBlock.TextProperty) && tb.Inlines.Count <= 1 && !(tb.Inlines.FirstInline is Run && BindingOperations.IsDataBound(tb.Inlines.FirstInline, Run.TextProperty)))
                {
                    if (o.Texto == null || tb.Text != o.TextoAplicado) o.Texto = tb.Text;
                    o.TextoAplicado = T(o.Texto); if (tb.Text != o.TextoAplicado) tb.Text = o.TextoAplicado;
                }
                else if (tb.Inlines.Count > 1) foreach (var inl in tb.Inlines) AplicarTrecho(inl);
                Fonte(o, tb, TextBlock.FontSizeProperty);
                Cor(o, tb, TextBlock.ForegroundProperty);
                return;
            }
            var cc = d as ContentControl;
            if (cc != null && !(d is Window) && (cc.Content is string || o.Conteudo != null) && !BindingOperations.IsDataBound(cc, ContentControl.ContentProperty))
            {
                if (o.Conteudo == null || !Equals(cc.Content, o.ConteudoAplicado)) o.Conteudo = cc.Content;
                if (o.Conteudo is string) { o.ConteudoAplicado = T((string)o.Conteudo); cc.Content = o.ConteudoAplicado; }
            }
            var w = d as Window;
            if (w != null)
            {
                if (o.Texto == null || w.Title != o.TextoAplicado) o.Texto = w.Title;
                o.TextoAplicado = T(o.Texto); w.Title = o.TextoAplicado;
                return;
            }
            var tx = d as TextBox;
            if (tx != null) { Fonte(o, tx, Control.FontSizeProperty); Pintar(o, tx, Control.BackgroundProperty, Control.BorderBrushProperty, 1); return; }
            var bt = d as Button;
            if (bt != null)
            {
                Pintar(o, bt, Control.BackgroundProperty, Control.BorderBrushProperty, 1);
                // botão de altura fixa cresce junto com o texto (senão as letras ficam cortadas)
                if (!double.IsNaN(bt.Height) && bt.Height > 0)
                {
                    if (o.Altura == 0 || Math.Abs(bt.Height - o.AlturaAplicada) > 0.01) o.Altura = bt.Height;
                    o.AlturaAplicada = Math.Round(o.Altura * fatorAtual); if (Math.Abs(bt.Height - o.AlturaAplicada) > 0.01) bt.Height = o.AlturaAplicada;
                }
                return;
            }
            // ícones (imagens e quadrinhos de tamanho fixo) crescem junto com o texto
            if ((d is Image || d is Border) && fe != null && !double.IsNaN(fe.Width) && !double.IsNaN(fe.Height) && fe.Width <= 64 && fe.Height <= 64 && !(fe.TemplatedParent is System.Windows.Controls.Primitives.Thumb))
            {
                if (o.Largura == 0 || Math.Abs(fe.Width - o.LarguraAplicada) > 0.01 || Math.Abs(fe.Height - o.AlturaAplicada) > 0.01) { o.Largura = fe.Width; o.Altura = fe.Height; }
                o.LarguraAplicada = Math.Round(o.Largura * fatorAtual, 1); o.AlturaAplicada = Math.Round(o.Altura * fatorAtual, 1);
                if (Math.Abs(fe.Width - o.LarguraAplicada) > 0.01) fe.Width = o.LarguraAplicada;
                if (Math.Abs(fe.Height - o.AlturaAplicada) > 0.01) fe.Height = o.AlturaAplicada;
            }
            var bd = d as Border;
            if (bd != null) Pintar(o, bd, Border.BackgroundProperty, Border.BorderBrushProperty, bd.Name == "PART_Fundo" || bd.Name == "PART_Titulo" ? Fundo : 1);
        }
        /** fundo e borda: contraste (e transparência no fundo da janela). Propriedade que vem do controle (TemplateBinding) fica com ele. */
        static void Pintar(Orig o, DependencyObject d, DependencyProperty fundo, DependencyProperty borda, double alfa)
        {
            if (!(d.ReadLocalValue(fundo) is TemplateBindingExpression))
            {
                var atual = d.GetValue(fundo) as Brush;
                if (o.Fundo == null || !ReferenceEquals(atual, o.FundoAplicado)) o.Fundo = atual;
                if (o.Fundo != null) { o.FundoAplicado = Trocar(o.Fundo, MapaFundo, alfa); if (!ReferenceEquals(atual, o.FundoAplicado)) d.SetValue(fundo, o.FundoAplicado); }
            }
            if (!(d.ReadLocalValue(borda) is TemplateBindingExpression))
            {
                var atual = d.GetValue(borda) as Brush;
                if (o.Borda == null || !ReferenceEquals(atual, o.BordaAplicada)) o.Borda = atual;
                if (o.Borda != null) { o.BordaAplicada = Trocar(o.Borda, MapaBorda); if (!ReferenceEquals(atual, o.BordaAplicada)) d.SetValue(borda, o.BordaAplicada); }
            }
        }
        static void AplicarTrecho(Inline inl)
        {
            var o = origs.GetValue(inl, k => new Orig());
            var r = inl as Run;
            if (r != null && !BindingOperations.IsDataBound(r, Run.TextProperty))
            {
                if (o.Texto == null || r.Text != o.TextoAplicado) o.Texto = r.Text;
                o.TextoAplicado = T(o.Texto); if (r.Text != o.TextoAplicado) r.Text = o.TextoAplicado;
            }
            if (inl.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue) Fonte(o, inl, TextElement.FontSizeProperty);
            if (inl.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue) Cor(o, inl, TextElement.ForegroundProperty);
        }
        static void Fonte(Orig o, DependencyObject d, DependencyProperty p)
        {
            var atual = (double)d.GetValue(p);
            if (o.Fonte == 0 || Math.Abs(atual - o.FonteAplicada) > 0.01) o.Fonte = atual;
            o.FonteAplicada = Math.Round(o.Fonte * fatorAtual, 1);
            if (Math.Abs(atual - o.FonteAplicada) > 0.01) d.SetValue(p, o.FonteAplicada);
        }
        static void Cor(Orig o, DependencyObject d, DependencyProperty p)
        {
            var atual = d.GetValue(p) as Brush;
            if (o.Cor == null || !ReferenceEquals(atual, o.CorAplicada)) o.Cor = atual;
            if (o.Cor == null) return;
            o.CorAplicada = Trocar(o.Cor, MapaTexto);
            if (!ReferenceEquals(atual, o.CorAplicada)) d.SetValue(p, o.CorAplicada);
        }

        /** depois de mudar uma opção: passa por tudo o que está nas janelas */
        public static void Reaplicar(IEnumerable<Window> janelas)
        {
            mexeu = true;
            foreach (var w in janelas.ToList()) Percorrer(w);
            foreach (Window w in Application.Current.Windows) Percorrer(w);
        }
        static void Percorrer(DependencyObject d)
        {
            if (d == null) return;
            var w = d as Window ?? Window.GetWindow(d);
            fatorAtual = FatorTexto * (w != null ? (double)w.GetValue(FatorJanelaProperty) : 1);
            Descer(d);
        }
        static void Descer(DependencyObject d)
        {
            Aplicar(d);
            int n = 0; try { n = VisualTreeHelper.GetChildrenCount(d); } catch { }
            for (int i = 0; i < n; i++) Descer(VisualTreeHelper.GetChild(d, i));
        }
    }
}
