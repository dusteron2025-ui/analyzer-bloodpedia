// Analyzer Bloodpedia — recursos extras: travar cliques (janelas "atravessáveis"), copiar resumo da hunt e aviso de nova versão.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
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
        public const string Versao = "1.0.5";
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
    // Ao abrir, consulta (GET) a página pública de versões do GitHub; não envia nenhum dado do jogo ou do usuário.
    // Com "Atualizar agora": baixa o zip da versão, confere o SHA-256 informado pelo GitHub, troca o próprio .exe
    // (o atual vira .antigo e é apagado na próxima abertura) e abre a versão nova.
    static class Atualizacao
    {
        public static void Verificar(Window dono, Action<string> log, Action antesDeSair)
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
                    var notas = Regex.Match(json, "\"body\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value.Replace("\\r", "").Replace("\\n", "\n").Replace("\\\"", "\"");
                    // o arquivo de nome fixo desta versão e a "impressão digital" (SHA-256) que o GitHub informa
                    var a = Regex.Match(json, "\"name\"\\s*:\\s*\"AnalyzerBloodpedia\\.zip\".*?\"digest\"\\s*:\\s*\"sha256:([0-9a-f]{64})\".*?\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Singleline);
                    if (tag.Length == 0 || !MaisNova(tag, Info.Versao)) { log("atualização: em dia (" + Info.Versao + ")"); return; }
                    log("atualização: nova versão " + tag);
                    var nova = new Versao { Numero = tag, Notas = notas, Url = a.Success ? a.Groups[2].Value : null, Sha256 = a.Success ? a.Groups[1].Value : null };
                    dono.Dispatcher.BeginInvoke(new Action(() => Perguntar(dono, nova, antesDeSair, log)));
                }
                catch (Exception ex) { log("atualização: não verificada (" + ex.Message + ")"); }
            }) { IsBackground = true }.Start();
        }
        class Versao { public string Numero, Notas, Url, Sha256; }
        static bool MaisNova(string a, string b)
        {
            Func<string, int[]> p = s => s.Split('.').Select(x => { int n; int.TryParse(Regex.Match(x, @"\d+").Value, out n); return n; }).ToArray();
            int[] x1 = p(a), x2 = p(b);
            for (int i = 0; i < Math.Max(x1.Length, x2.Length); i++) { int u = i < x1.Length ? x1[i] : 0, v = i < x2.Length ? x2[i] : 0; if (u != v) return u > v; }
            return false;
        }

        static string ExeAtual { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        /** ao abrir: apaga o .exe antigo que ficou da última atualização */
        public static void Limpar()
        {
            try { var velho = ExeAtual + ".antigo"; for (int i = 0; i < 10 && File.Exists(velho); i++) { try { File.Delete(velho); } catch { Thread.Sleep(300); } } } catch { }
        }
        /** depois de uma atualização, espera a versão anterior fechar antes de abrir a nova */
        public static void EsperarAnterior()
        {
            try
            {
                var eu = Process.GetCurrentProcess();
                for (int i = 0; i < 40; i++)
                {
                    if (!Process.GetProcessesByName(eu.ProcessName).Any(p => p.Id != eu.Id)) break;
                    Thread.Sleep(150);
                }
            }
            catch { }
        }

        static void Perguntar(Window dono, Versao v, Action antesDeSair, Action<string> log)
        {
            var w = new Window { Title = "Nova versão", Width = 270, Owner = dono, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true, Tag = new SolidColorBrush(Color.FromRgb(0xC2, 0x5C, 0xE0)), Uid = "" };
            w.Style = (Style)Application.Current.Resources["JanelaAnalyzer"];
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = "Versão " + v.Numero + " disponível", FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            var info = new TextBlock { Text = "Você está usando a " + Info.Versao + ". O analyzer baixa a nova versão, troca sozinho e abre de novo.", Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            st.Children.Add(info);
            if (v.Notas.Trim().Length > 0) st.Children.Add(new TextBlock { Text = v.Notas.Trim(), Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD4)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxHeight = 160 });
            var barra = new ProgressBar { Style = (Style)Application.Current.Resources["BarraFina"], Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0x5C, 0xE0)), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            st.Children.Add(barra);
            var atualizar = new Button { Content = "Atualizar agora", Style = (Style)Application.Current.Resources["BotaoAtalho"] };
            var depois = new Button { Content = "Agora não", Style = (Style)Application.Current.Resources["BotaoAtalho"] };
            var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            linha.Children.Add(depois); linha.Children.Add(atualizar); st.Children.Add(linha);
            w.Content = st;
            depois.Click += (s, e) => w.Close();
            atualizar.Click += (s, e) =>
            {
                if (v.Url == null) { try { Process.Start("https://github.com/" + Info.Repositorio + "/releases/latest"); } catch { } w.Close(); return; }
                atualizar.IsEnabled = false; depois.IsEnabled = false; barra.Visibility = Visibility.Visible; info.Text = "Baixando…";
                var tmp = Path.Combine(Path.GetTempPath(), "AnalyzerBloodpedia-" + v.Numero + ".zip");
                var wc = new WebClient(); wc.Headers.Add("User-Agent", "AnalyzerBloodpedia");
                wc.DownloadProgressChanged += (s2, e2) => { barra.Value = e2.ProgressPercentage; info.Text = string.Format("Baixando… {0:0.0} de {1:0.0} MB", e2.BytesReceived / 1048576.0, e2.TotalBytesToReceive / 1048576.0); };
                wc.DownloadFileCompleted += (s2, e2) =>
                {
                    string erro = e2.Error != null ? "Não deu para baixar: " + e2.Error.Message : Instalar(tmp, v.Sha256, log);
                    try { File.Delete(tmp); } catch { }
                    if (erro != null)
                    {
                        info.Text = erro + " Você pode baixar pelo site e trocar o .exe à mão.";
                        atualizar.Content = "Abrir o site"; atualizar.IsEnabled = true; depois.IsEnabled = true; barra.Visibility = Visibility.Collapsed;
                        v.Url = null; return;
                    }
                    info.Text = "Pronto! Abrindo a versão " + v.Numero + "…";
                    try { if (antesDeSair != null) antesDeSair(); } catch { }
                    try { Process.Start(new ProcessStartInfo(ExeAtual, "--apos-atualizar") { UseShellExecute = true }); }
                    catch (Exception ex) { log("atualização: não reabriu (" + ex.Message + ")"); }
                    Application.Current.Shutdown();
                };
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                wc.DownloadFileAsync(new Uri(v.Url), tmp);
            };
            w.Loaded += (s, e) =>
            {
                w.ApplyTemplate();
                ((Button)w.Template.FindName("PART_Fechar", w)).Click += (s2, e2) => { if (depois.IsEnabled) w.Close(); };
                ((FrameworkElement)w.Template.FindName("PART_Recolher", w)).Visibility = Visibility.Collapsed;
                ((FrameworkElement)w.Template.FindName("PART_Titulo", w)).MouseLeftButtonDown += (s2, e2) => w.DragMove();
            };
            w.Show();
        }

        /** confere o zip (SHA-256 do GitHub), tira o .exe de dentro e troca o atual; devolve null se deu certo ou a mensagem de erro */
        public static string Instalar(string zip, string sha256, Action<string> log)
        {
            try
            {
                if (sha256 != null)
                {
                    string h; using (var f = File.OpenRead(zip)) h = BitConverter.ToString(SHA256.Create().ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                    if (h != sha256) { log("atualização: SHA-256 não bate (" + h + ")"); return "O arquivo baixado veio diferente do publicado."; }
                }
                var exe = ExeAtual; var novo = exe + ".novo"; var velho = exe + ".antigo";
                using (var z = ZipFile.OpenRead(zip))
                {
                    var e = z.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').EndsWith("/AnalyzerBloodpedia.exe", StringComparison.OrdinalIgnoreCase) || x.FullName.Equals("AnalyzerBloodpedia.exe", StringComparison.OrdinalIgnoreCase));
                    if (e == null || e.Length < 1000000) return "O pacote baixado não tem o programa.";
                    if (File.Exists(novo)) File.Delete(novo);
                    e.ExtractToFile(novo);
                }
                using (var f = File.OpenRead(novo)) { if (f.ReadByte() != 'M' || f.ReadByte() != 'Z') { f.Close(); File.Delete(novo); return "O pacote baixado não tem um programa válido."; } }
                // o Windows deixa renomear o programa que está aberto: o atual vira .antigo e o novo assume o nome
                if (File.Exists(velho)) File.Delete(velho);
                File.Move(exe, velho);
                try { File.Move(novo, exe); }
                catch { File.Move(velho, exe); throw; }
                log("atualização: instalada");
                return null;
            }
            catch (Exception ex) { log("atualização: falhou (" + ex.Message + ")"); return "Não deu para instalar: " + ex.Message + "."; }
        }
    }
}
