// Analyzer Bloodpedia — recursos extras: travar cliques (janelas "atravessáveis"), copiar resumo da hunt e aviso de nova versão.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace BloodstoneHunt
{
    static class Info
    {
        public const string Nome = "Analyzer Bloodpedia";
        public const string Versao = "1.0.0";
        // repositório público onde ficam as versões (GitHub Releases)
        public const string Repositorio = "dusteron2025-ui/analyzer-bloodpedia";
    }

    // ---------------- travar cliques ----------------
    // Com a trava ligada, todas as janelas do analyzer deixam os cliques passarem para o jogo (WS_EX_TRANSPARENT).
    // Para destravar: o botão de cadeado que fica flutuando no lugar do botão original, ou o atalho Ctrl+Shift+L.
    class Trava
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WM_HOTKEY = 0x312, ID = 0xB10D;

        readonly Func<IEnumerable<Window>> janelas; readonly Button botao; readonly Window dono;
        Window flutuante; public bool Ligada { get; private set; }
        public event Action Mudou;

        public Trava(Window dono, Button botao, Func<IEnumerable<Window>> janelas)
        {
            this.dono = dono; this.botao = botao; this.janelas = janelas;
            dono.SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(dono).Handle;
                RegisterHotKey(h, ID, 0x2 | 0x4, 0x4C);   // Ctrl+Shift+L
                HwndSource.FromHwnd(h).AddHook((IntPtr hw, int msg, IntPtr wp, IntPtr lp, ref bool tratado) =>
                {
                    if (msg == WM_HOTKEY && wp.ToInt32() == ID) { Alternar(); tratado = true; }
                    return IntPtr.Zero;
                });
            };
            dono.Closed += (s, e) => { try { UnregisterHotKey(new WindowInteropHelper(dono).Handle, ID); } catch { } if (flutuante != null) flutuante.Close(); };
            dono.LocationChanged += (s, e) => Posicionar();
        }

        public void Alternar()
        {
            Ligada = !Ligada;
            foreach (var w in janelas()) Aplicar(w);
            if (Ligada) MostrarFlutuante(); else if (flutuante != null) flutuante.Hide();
            var m = Mudou; if (m != null) m();
        }
        public void Aplicar(Window w)
        {
            if (w == null || !w.IsLoaded) return;
            var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
            int ex = GetWindowLong(h, GWL_EXSTYLE);
            if (Ligada) ex |= WS_EX_TRANSPARENT; else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(h, GWL_EXSTYLE, ex);
        }
        void MostrarFlutuante()
        {
            if (flutuante == null)
            {
                var b = new Button { Content = "", Style = (Style)Application.Current.Resources["BotaoTitulo"], Width = 18, Height = 18, Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xB5, 0x44)), ToolTip = "Destravar o analyzer (Ctrl+Shift+L)" };
                b.Click += (s, e) => Alternar();
                flutuante = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = true, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
                    Width = 22, Height = 22, Background = Brushes.Transparent,
                    Content = new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(0x30, 0x24, 0x10)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xF2, 0xB5, 0x44)), BorderThickness = new Thickness(1), Child = b }
                };
            }
            Posicionar(); flutuante.Show();
        }
        void Posicionar()
        {
            if (flutuante == null || !botao.IsVisible) return;
            try
            {
                var p = botao.PointToScreen(new Point(0, 0));
                var src = PresentationSource.FromVisual(botao);
                if (src != null) p = src.CompositionTarget.TransformFromDevice.Transform(p);
                flutuante.Left = p.X - 2; flutuante.Top = p.Y - 2;
            }
            catch { }
        }
    }

    // ---------------- aviso de nova versão ----------------
    // Só faz uma consulta (GET) à página pública de versões do GitHub ao abrir. Não envia nenhum dado do jogo ou do usuário.
    static class Atualizacao
    {
        public static void Verificar(Window dono, Action<string> log)
        {
            new Thread(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
                    var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Info.Repositorio + "/releases/latest");
                    req.UserAgent = "AnalyzerBloodpedia"; req.Timeout = 8000;
                    string json; using (var r = new StreamReader(req.GetResponse().GetResponseStream())) json = r.ReadToEnd();
                    var tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"").Groups[1].Value;
                    var url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+/releases/[^\"]+)\"").Groups[1].Value;
                    var notas = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value.Replace("\\r", "").Replace("\\n", "\n").Replace("\\\"", "\"");
                    if (tag.Length == 0 || !MaisNova(tag, Info.Versao)) { log("atualização: em dia (" + Info.Versao + ")"); return; }
                    log("atualização: nova versão " + tag);
                    dono.Dispatcher.BeginInvoke(new Action(() => Perguntar(dono, tag, url, notas)));
                }
                catch (Exception ex) { log("atualização: não verificada (" + ex.Message + ")"); }
            }) { IsBackground = true }.Start();
        }
        static bool MaisNova(string a, string b)
        {
            Func<string, int[]> p = s => s.Split('.').Select(x => { int n; int.TryParse(Regex.Match(x, @"\d+").Value, out n); return n; }).ToArray();
            int[] x1 = p(a), x2 = p(b);
            for (int i = 0; i < Math.Max(x1.Length, x2.Length); i++) { int u = i < x1.Length ? x1[i] : 0, v = i < x2.Length ? x2[i] : 0; if (u != v) return u > v; }
            return false;
        }
        static void Perguntar(Window dono, string versao, string url, string notas)
        {
            var w = new Window { Title = "Nova versão", Width = 260, Owner = dono, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true, Tag = new SolidColorBrush(Color.FromRgb(0xC2, 0x5C, 0xE0)), Uid = "" };
            w.Style = (Style)Application.Current.Resources["JanelaAnalyzer"];
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = "Versão " + versao + " disponível", FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            st.Children.Add(new TextBlock { Text = "Você está usando a " + Info.Versao + ". Baixe a nova versão e substitua a pasta do analyzer.", Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            if (notas.Trim().Length > 0) st.Children.Add(new TextBlock { Text = notas.Trim(), Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD4)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 160 });
            var baixar = new Button { Content = "Baixar", Style = (Style)Application.Current.Resources["BotaoAtalho"] };
            var depois = new Button { Content = "Agora não", Style = (Style)Application.Current.Resources["BotaoAtalho"] };
            var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            linha.Children.Add(depois); linha.Children.Add(baixar); st.Children.Add(linha);
            w.Content = st;
            baixar.Click += (s, e) => { try { Process.Start(url.Length > 0 ? url : "https://github.com/" + Info.Repositorio + "/releases/latest"); } catch { } w.Close(); };
            depois.Click += (s, e) => w.Close();
            w.Loaded += (s, e) =>
            {
                w.ApplyTemplate();
                ((Button)w.Template.FindName("PART_Fechar", w)).Click += (s2, e2) => w.Close();
                ((FrameworkElement)w.Template.FindName("PART_Recolher", w)).Visibility = Visibility.Collapsed;
                ((FrameworkElement)w.Template.FindName("PART_Titulo", w)).MouseLeftButtonDown += (s2, e2) => w.DragMove();
            };
            w.Show();
        }
    }
}
