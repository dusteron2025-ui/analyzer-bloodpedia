// Analyzer Bloodpedia — imagens de tutorial da Hunt em grupo (só na compilação de teste).
//   teste-captura.exe --tutorial-grupo <pasta>   →  tutorial-lider.png e tutorial-participante.png (1080 px de largura)
// Usa prints reais das janelas do analyzer (o botão, a janela de entrar/criar e a Party Hunt) com dados de exemplo.
#if TESTE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BloodstoneHunt
{
    static class TutorialTeste { public static bool SemChaveSalva; }

    partial class Programa
    {
        // ---------------- prints das janelas em 2x ----------------
        static BitmapSource Print2x(Window w)
        {
            if (!w.IsVisible) { w.Left = -10000; w.ShowInTaskbar = false; w.Show(); }
            w.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); w.UpdateLayout();
            var raiz = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            var rtb = new RenderTargetBitmap((int)w.ActualWidth * 2, (int)w.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32); rtb.Render(raiz);
            rtb.Freeze(); return rtb;
        }
        static IEnumerable<T> Filhos<T>(DependencyObject d) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is T) yield return (T)c;
                foreach (var x in Filhos<T>(c)) yield return x;
            }
        }
        void GrupoExemplo(bool souLider)
        {
            long agora = 1800000000000, criado = agora - 47 * 60000;
            var json = "{\"codigo\":\"KX7P2M\",\"criado\":" + criado + ",\"agora\":" + agora + ",\"lider\":\"aaaaaaaa\",\"epoca\":0,\"membros\":[" +
                "{\"id\":\"aaaaaaaa\",\"nome\":\"Eduzinho\",\"lider\":true,\"atualizado\":" + (agora - 60000) + ",\"dados\":{\"loot\":182400,\"supplies\":21300,\"dano\":52800,\"cura\":4100}}," +
                "{\"id\":\"bbbbbbbb\",\"nome\":\"Arqueira Lia\",\"lider\":false,\"atualizado\":" + (agora - 60000) + ",\"dados\":{\"loot\":0,\"supplies\":18700,\"dano\":41200,\"cura\":9850}}," +
                "{\"id\":\"cccccccc\",\"nome\":\"Xamã Torr\",\"lider\":false,\"atualizado\":" + (agora - 120000) + ",\"dados\":{\"loot\":0,\"supplies\":15900,\"dano\":36700,\"cura\":12300}}]}";
            grupoAtual = souLider ? new[] { "KX7P2M", "aaaaaaaa", "x", "Eduzinho" } : new[] { "KX7P2M", "bbbbbbbb", "x", "Arqueira Lia" };
            grupoEstado = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            grupoUltimoEnvio = DateTime.Now.AddMinutes(-1);
        }
        BitmapSource PrintDialogo(bool lider)
        {
            grupoAtual = null; TutorialTeste.SemChaveSalva = true; AbrirGrupo();
            var w = Application.Current.Windows.OfType<Window>().First(x => x.Title == "Caçada em grupo");
            w.Left = -10000; w.ShowInTaskbar = false;
            var caixas = Filhos<TextBox>((DependencyObject)w.Content).ToList();   // nome, código, chave
            caixas[0].Text = lider ? "Eduzinho" : "Arqueira Lia";
            if (lider) caixas[2].Text = "GRP-7K2M-Q9XA"; else caixas[1].Text = "KX7P2M";
            var img = Print2x(w); w.Close(); return img;
        }

        // ---------------- montagem da imagem ----------------
        static readonly FontFamily FonteTut = new FontFamily("Segoe UI");
        static Brush C2(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }
        static TextBlock Txt(string t, double tam, string cor, FontWeight peso) { return new TextBlock { Text = t, FontSize = tam, Foreground = C2(cor), FontWeight = peso, FontFamily = FonteTut, TextWrapping = TextWrapping.Wrap }; }
        /** texto com **negrito** simples */
        static TextBlock Rico(string t, double tam, string cor, string corForte)
        {
            var tb = new TextBlock { FontSize = tam, Foreground = C2(cor), FontFamily = FonteTut, TextWrapping = TextWrapping.Wrap, LineHeight = tam * 1.45 };
            var partes = t.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < partes.Length; i++) tb.Inlines.Add(new Run(partes[i]) { Foreground = i % 2 == 1 ? C2(corForte) : C2(cor), FontWeight = i % 2 == 1 ? FontWeights.SemiBold : FontWeights.Normal });
            return tb;
        }
        class PassoTut { public string Titulo, Texto; public BitmapSource Imagem; public double Escala = 1; }

        FrameworkElement MontarTutorial(string selo, string titulo, string sub, string corSelo, List<PassoTut> passos, string[] dicas)
        {
            var raiz = new Grid { Width = 1080, Background = C2("#0F0C16") };
            TextOptions.SetTextFormattingMode(raiz, TextFormattingMode.Ideal);
            var luz = new RadialGradientBrush { Center = new Point(0.5, 0), GradientOrigin = new Point(0.5, 0), RadiusX = 0.9, RadiusY = 0.25 };
            luz.GradientStops.Add(new GradientStop(Color.FromArgb(40, 179, 140, 255), 0)); luz.GradientStops.Add(new GradientStop(Color.FromArgb(0, 179, 140, 255), 1));
            raiz.Children.Add(new System.Windows.Shapes.Rectangle { Fill = luz });
            var s = new StackPanel { Margin = new Thickness(56, 52, 56, 44) };
            raiz.Children.Add(s);

            // topo: marca + título
            var topo = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 26) };
            var logo = LogoMarca(); if (logo != null) { var im = new Image { Source = logo, Height = 64, Margin = new Thickness(0, 0, 12, 0) }; RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality); topo.Children.Add(im); }
            var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nm.Children.Add(Txt("BLOODPEDIA", 28, "#F3F3F7", FontWeights.Bold));
            nm.Children.Add(Txt("HUNT ANALYZER", 12, "#7D7396", FontWeights.SemiBold));
            topo.Children.Add(nm); s.Children.Add(topo);

            var pill = new Border { CornerRadius = new CornerRadius(100), Background = C2(corSelo), Padding = new Thickness(16, 6, 18, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
            pill.Child = Txt(selo, 16, "#1A1030", FontWeights.Bold); s.Children.Add(pill);
            s.Children.Add(Txt(titulo, 54, "#F3F3F7", FontWeights.Bold));
            var st = Txt(sub, 22, "#B9B0CC", FontWeights.Normal); st.Margin = new Thickness(0, 6, 0, 30); s.Children.Add(st);

            int n = 1;
            foreach (var p in passos)
            {
                var card = new Border { CornerRadius = new CornerRadius(24), Background = C2("#1A1426"), BorderBrush = C2("#22FFFFFF"), BorderThickness = new Thickness(1), Padding = new Thickness(30, 26, 30, 28), Margin = new Thickness(0, 0, 0, 22) };
                var cs = new StackPanel();
                var cab = new Grid(); cab.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); cab.ColumnDefinitions.Add(new ColumnDefinition());
                var num = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = C2("#B38CFF"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 18, 0) };
                var nt = Txt((n++).ToString(), 22, "#1A1030", FontWeights.Bold); nt.HorizontalAlignment = HorizontalAlignment.Center; nt.VerticalAlignment = VerticalAlignment.Center; num.Child = nt;
                cab.Children.Add(num);
                var tx = new StackPanel(); Grid.SetColumn(tx, 1);
                tx.Children.Add(Txt(p.Titulo, 28, "#F3F3F7", FontWeights.Bold));
                var corpo = Rico(p.Texto, 21, "#B9B0CC", "#F3F3F7"); corpo.Margin = new Thickness(0, 6, 0, 0); tx.Children.Add(corpo);
                cab.Children.Add(tx); cs.Children.Add(cab);
                if (p.Imagem != null)
                {
                    var moldura = new Border { CornerRadius = new CornerRadius(18), Background = C2("#0F0C16"), Padding = new Thickness(18), Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
                    var im = new Image { Source = p.Imagem, Width = p.Imagem.PixelWidth / 2.0 * p.Escala, Height = p.Imagem.PixelHeight / 2.0 * p.Escala };
                    RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                    moldura.Child = im; cs.Children.Add(moldura);
                }
                card.Child = cs; s.Children.Add(card);
            }
            if (dicas.Length > 0)
            {
                var d = new Border { CornerRadius = new CornerRadius(20), Background = C2("#1F1830"), BorderBrush = C2("#4A3A66"), BorderThickness = new Thickness(1), Padding = new Thickness(28, 22, 28, 22), Margin = new Thickness(0, 4, 0, 22) };
                var ds = new StackPanel();
                ds.Children.Add(Txt("💡 Dicas", 24, "#C9ABFF", FontWeights.Bold));
                foreach (var x in dicas) { var t = Rico("•  " + x, 20, "#B9B0CC", "#F3F3F7"); t.Margin = new Thickness(0, 8, 0, 0); ds.Children.Add(t); }
                d.Child = ds; s.Children.Add(d);
            }
            var rod = new TextBlock { FontFamily = FonteTut, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            rod.Inlines.Add(new Run("Dúvidas? Fale com a equipe no Discord da Bloodpedia  ·  ") { Foreground = C2("#7D7396") });
            rod.Inlines.Add(new Run("bloodpedia.com.br/hunt-analyzer") { Foreground = C2("#B38CFF") });
            s.Children.Add(rod);
            return raiz;
        }
        static void SalvarTutorial(FrameworkElement el, string arq)
        {
            el.Measure(new Size(1080, double.PositiveInfinity));
            var h = Math.Ceiling(el.DesiredSize.Height);
            el.Arrange(new Rect(0, 0, 1080, h)); el.UpdateLayout();
            var rtb = new RenderTargetBitmap(1080, (int)h, 96, 96, PixelFormats.Pbgra32); rtb.Render(el);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = File.Create(arq)) enc.Save(f);
        }

        public void TutorialGrupo(string pasta)
        {
            Carregar(); P.PorCima = false;
            foreach (var d in Definicoes) Criar(d);
            grupoTimer = new DispatcherTimer();   // não chama IniciarGrupo: nada é enviado a nenhum grupo de verdade
            // botão "Hunt em grupo": recorte do pé da janela do Hunt Analyzer
            var hunt = Print2x(janelas["hunt"].W);
            int alt = 168; var botao = new CroppedBitmap(hunt, new Int32Rect(0, hunt.PixelHeight - alt, hunt.PixelWidth, alt));
            var dlgLider = PrintDialogo(true);
            var dlgMembro = PrintDialogo(false);
            GrupoExemplo(true); MostrarJanelaGrupo(); var partyLider = Print2x(janelaGrupo.W);
            var codigo = new CroppedBitmap(partyLider, new Int32Rect(0, 0, partyLider.PixelWidth, 150));   // título + código + copiar
            GrupoExemplo(false); DesenharGrupo(); var partyMembro = Print2x(janelaGrupo.W);

            var lider = MontarTutorial("PARA O LÍDER", "Hunt em grupo", "Crie o grupo, passe o código para a party (até 6 pessoas) e acompanhem a caçada juntos.", "#F5C35B", new List<PassoTut> {
                new PassoTut { Titulo = "Abra o Hunt em grupo", Texto = "Atualize o analyzer para a versão **1.0.10 ou mais nova** (ao abrir, clique em **Atualizar agora**). No Hunt Analyzer, clique em **Hunt em grupo**.", Imagem = botao, Escala = 2.0 },
                new PassoTut { Titulo = "Crie o grupo com a sua chave", Texto = "Digite o **nome do seu personagem**. Em **Criar um grupo (líder)**, cole a **chave de líder** que a equipe te mandou e clique em **Ativar e criar**. A chave é só sua e vale em **1 PC**.", Imagem = dlgLider, Escala = 1.25 },
                new PassoTut { Titulo = "Passe o código para a party", Texto = "A janela **Party Hunt** abre com o **código de 6 letras** (ex.: KX7P2M). Clique no ícone ao lado dele para copiar e mande para quem vai caçar com você.", Imagem = codigo, Escala = 1.8 },
                new PassoTut { Titulo = "Cacem e acompanhem", Texto = "Os números de todos chegam a **cada 1 minuto** (ou em **Enviar agora**). Você vê o loot, os supplies e o balanço do grupo e o balanço, o dano e a cura de cada um. No fim, a janela mostra **quanto você paga para cada um** para todos lucrarem igual. O **✕** no cartão tira alguém do grupo, **Zerar** recomeça a contagem para todos e **Encerrar** termina o grupo.", Imagem = partyLider, Escala = 1.25 },
            }, new[] {
                "Deixe a aba **Pilhagem** ativa no chat: só o **loot do líder** conta, para nada ser contado em dobro.",
                "Abre o Bloodstone **como administrador**? Abra o analyzer do mesmo jeito (o analyzer avisa se precisar).",
                "Fechou o analyzer sem querer? Abra de novo: ele **volta sozinho** para o grupo.",
            });
            SalvarTutorial(lider, Path.Combine(pasta, "tutorial-lider.png"));

            var membro = MontarTutorial("PARA QUEM VAI PARTICIPAR", "Hunt em grupo", "Entre no grupo do líder com o código e acompanhe a caçada da party. Não precisa de chave.", "#B38CFF", new List<PassoTut> {
                new PassoTut { Titulo = "Abra o Hunt em grupo", Texto = "Atualize o analyzer para a versão **1.0.10 ou mais nova** (ao abrir, clique em **Atualizar agora**). No Hunt Analyzer, clique em **Hunt em grupo**.", Imagem = botao, Escala = 2.0 },
                new PassoTut { Titulo = "Entre com o código do líder", Texto = "Digite o **nome do seu personagem**. Em **Entrar num grupo**, digite o **código de 6 letras** que o líder passou e clique em **Entrar**.", Imagem = dlgMembro, Escala = 1.25 },
                new PassoTut { Titulo = "Cace normalmente", Texto = "Seus números (supplies, dano e cura) vão para o grupo **a cada 1 minuto**, ou na hora em **Enviar agora**. O seu cartão aparece com **(você)**. No fim, a janela mostra **quanto o líder te paga** para todos lucrarem igual. Para sair, clique em **Sair**.", Imagem = partyMembro, Escala = 1.25 },
            }, new[] {
                "O **loot é contado pelo líder**: por isso o seu balanço aparece negativo até a divisão no fim.",
                "Abre o Bloodstone **como administrador**? Abra o analyzer do mesmo jeito (o analyzer avisa se precisar).",
                "No grupo vão só o seu **nome** e os números da caçada, apagados 6 h depois. Nada da sua conta ou do seu PC.",
            });
            SalvarTutorial(membro, Path.Combine(pasta, "tutorial-participante.png"));
        }
    }
}
#endif
