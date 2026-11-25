// Analyzer Bloodpedia — caçada em grupo (Party Hunt), servidor separado da wiki: https://grupo.bloodpedia.com.br
//
// - Só líderes liberados pela equipe (chave de acesso) criam grupos; qualquer pessoa entra com o código de 6 letras.
// - Até 6 pessoas. Cada analyzer envia os próprios números a cada 2 minutos (e em "Enviar agora" e ao fechar).
// - Cada analyzer só vê o loot que a própria pessoa pegou, então o loot do grupo é a soma do de todos;
//   supplies, dano e cura também são de cada um.
// - Vai para o servidor só o essencial: o nome que a pessoa digitou, loot, supplies, dano e cura (e, para o líder, um
//   hash do PC feito só para o modo grupo, que limita a chave a 1 PC). Ficam lá por 6 h desde a última atualização.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BloodstoneHunt
{
    // ---------------- conversa com o servidor do grupo ----------------
    static class GrupoServidor
    {
        const string Servidor = "https://grupo.bloodpedia.com.br/api/";
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public static string Pasta { get { var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BloodstoneHunt"); Directory.CreateDirectory(p); return p; } }

        /** POST JSON; devolve (status, objeto). Erro de rede vira status 0 com "erro". */
        public static Dictionary<string, object> Post(string rota, Dictionary<string, object> corpo, out int status, int timeout = 10000)
        {
            status = 0;
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                var req = (HttpWebRequest)WebRequest.Create(Servidor + rota);
                req.Method = "POST"; req.ContentType = "application/json"; req.UserAgent = "AnalyzerBloodpedia/" + Info.Versao; req.Timeout = timeout; req.ReadWriteTimeout = timeout;
                var b = Encoding.UTF8.GetBytes(Json.Serialize(corpo));
                using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                HttpWebResponse r;
                try { r = (HttpWebResponse)req.GetResponse(); }
                catch (WebException e) { r = e.Response as HttpWebResponse; if (r == null) throw; }
                using (r) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                {
                    status = (int)r.StatusCode;
                    var t = sr.ReadToEnd();
                    try { return Json.Deserialize<Dictionary<string, object>>(t) ?? new Dictionary<string, object>(); }
                    catch { return new Dictionary<string, object> { { "erro", "Resposta inesperada do servidor (" + status + ")." } }; }
                }
            }
            catch (Exception e) { return new Dictionary<string, object> { { "erro", "Sem conexão com o servidor do grupo (" + e.Message + ")." } }; }
        }
        public static string Txt(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o != null ? Convert.ToString(o) : null; }
        public static long Num(Dictionary<string, object> d, string k) { object o; if (d == null || !d.TryGetValue(k, out o) || o == null) return 0; try { return Convert.ToInt64(o); } catch { return 0; } }
        public static bool Sim(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) && o is bool && (bool)o; }
        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as Dictionary<string, object> : null; }
        public static IEnumerable<Dictionary<string, object>> Lista(Dictionary<string, object> d, string k)
        {
            object o; if (d == null || !d.TryGetValue(k, out o) || !(o is System.Collections.IEnumerable)) yield break;
            foreach (var x in (System.Collections.IEnumerable)o) { var m = x as Dictionary<string, object>; if (m != null) yield return m; }
        }

        // ---------------- o que fica guardado no PC ----------------
        static string ArqAcesso { get { return Path.Combine(Pasta, "grupo-acesso.dat"); } }
        static string ArqGrupo { get { return Path.Combine(Pasta, "grupo.dat"); } }
        public static string TokenAcesso { get { try { return File.Exists(ArqAcesso) ? File.ReadAllText(ArqAcesso).Trim() : null; } catch { return null; } } set { try { if (value == null) File.Delete(ArqAcesso); else File.WriteAllText(ArqAcesso, value); } catch { } } }
        /** grupo atual: código, membro, segredo, nome (para voltar sozinho ao reabrir o analyzer) */
        public static string[] Atual
        {
            get { try { var p = File.Exists(ArqGrupo) ? File.ReadAllText(ArqGrupo, Encoding.UTF8).Split('\n') : null; return p != null && p.Length >= 4 ? p.Select(x => x.Trim()).ToArray() : null; } catch { return null; } }
            set { try { if (value == null) File.Delete(ArqGrupo); else File.WriteAllText(ArqGrupo, string.Join("\n", value), Encoding.UTF8); } catch { } }
        }
        public static string ArqNome { get { return Path.Combine(Pasta, "grupo-nome.txt"); } }
    }

    partial class Programa
    {
        const int IntervaloGrupo = 120;   // segundos entre os envios
        string[] grupoAtual;              // código, membro, segredo, nome
        Dictionary<string, object> grupoEstado; DateTime grupoUltimoEnvio = DateTime.MinValue; string grupoErro;
        DispatcherTimer grupoTimer; volatile bool grupoEnviando; Janela janelaGrupo; StackPanel grupoCorpo;
        // O grupo conta a partir de um "ponto de partida": ao entrar/criar, quando o líder zera (nova época) ou quando a
        // própria sessão é zerada. Enviado = número da caçada − base (a base pode ser negativa para manter o que já
        // tinha sido contado, por exemplo depois de reabrir o analyzer ou zerar só a própria sessão).
        long grupoEpoca = -1; Dictionary<string, long> grupoBase, grupoUltimoEnviado;
        static readonly string[] CamposGrupo = { "loot", "supplies", "dano", "cura" };

        bool EmGrupo { get { return grupoAtual != null; } }

        Dictionary<string, long> NumerosCacada()
        {
            var d = ColetarRelatorio();
            return new Dictionary<string, long> { { "loot", d.Loot }, { "supplies", d.Supplies }, { "dano", d.Dano }, { "cura", d.Cura } };
        }
        /** recomeça a contagem do grupo a partir de agora (mantendo "manter", se vier) */
        void NovaBaseGrupo(Dictionary<string, long> manter)
        {
            var agora = NumerosCacada();
            grupoBase = CamposGrupo.ToDictionary(k => k, k => agora[k] - (manter != null && manter.ContainsKey(k) ? manter[k] : 0));
        }
        /** a sessão local foi zerada no Hunt Analyzer: o grupo continua com o que já tinha recebido */
        void SessaoLocalZerada()
        {
            if (!EmGrupo) return;
            grupoBase = CamposGrupo.ToDictionary(k => k, k => -(grupoUltimoEnviado != null && grupoUltimoEnviado.ContainsKey(k) ? grupoUltimoEnviado[k] : 0));
        }
        void ReenviarLogo()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(21) };   // o servidor aceita um envio a cada 20 s
            t.Tick += (s, e) => { t.Stop(); EnviarGrupo(false); }; t.Start();
        }

        // chamado ao abrir: se estava num grupo, volta a ele
        void IniciarGrupo()
        {
            grupoAtual = GrupoServidor.Atual;
            grupoEpoca = -1; grupoBase = null;   // o primeiro envio só pergunta a época; depois continua de onde parou
            grupoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(IntervaloGrupo) };
            grupoTimer.Tick += (s, e) => EnviarGrupo(false);
            var t2 = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) }; t2.Tick += (s, e) => { if (EmGrupo) DesenharGrupo(); }; t2.Start();   // "há X min"
            if (EmGrupo) { grupoTimer.Start(); EnviarGrupo(false); }
        }

        /** só o que o painel do grupo usa: loot, supplies em gp, dano e cura */
        Dictionary<string, object> DadosGrupo()
        {
            var agora = NumerosCacada();
            var env = CamposGrupo.ToDictionary(k => k, k => Math.Max(0, agora[k] - (grupoBase != null ? grupoBase[k] : 0)));
            grupoUltimoEnviado = env;
            return env.ToDictionary(x => x.Key, x => (object)x.Value);
        }

        void EnviarGrupo(bool final)
        {
            if (!EmGrupo || grupoEnviando) return;
            grupoEnviando = true;
            var g = grupoAtual; var corpo = new Dictionary<string, object> { { "acao", "enviar" }, { "codigo", g[0] }, { "membro", g[1] }, { "segredo", g[2] }, { "epoca", grupoEpoca }, { "dados", DadosGrupo() } };
            Action rodar = () =>
            {
                int st; var r = GrupoServidor.Post("grupo", corpo, out st, final ? 3000 : 10000);
                grupoEnviando = false;
                if (final) return;
                janelas["hunt"].W.Dispatcher.BeginInvoke(new Action(() => RespostaGrupo(st, r)));
            };
            if (final) rodar(); else new Thread(() => rodar()) { IsBackground = true, Name = "grupo" }.Start();
        }
        void RespostaGrupo(int st, Dictionary<string, object> r)
        {
            if (st == 200)
            {
                grupoEstado = GrupoServidor.Obj(r, "estado"); grupoUltimoEnvio = DateTime.Now; grupoErro = null;
                long ep = GrupoServidor.Num(grupoEstado, "epoca");
                if (grupoEpoca < 0)
                {
                    // reabriu o analyzer: continua somando ao que o grupo já tinha de mim
                    var eu = GrupoServidor.Lista(grupoEstado, "membros").FirstOrDefault(m => GrupoServidor.Txt(m, "id") == grupoAtual[1]);
                    var meus = GrupoServidor.Obj(eu, "dados");
                    grupoEpoca = ep; NovaBaseGrupo(meus == null ? null : CamposGrupo.ToDictionary(k => k, k => GrupoServidor.Num(meus, k)));
                    ReenviarLogo();
                }
                else if (ep != grupoEpoca)
                {
                    // o líder zerou a sessão do grupo: conta a partir de agora
                    grupoEpoca = ep; NovaBaseGrupo(null); Log("grupo: sessão zerada pelo líder");
                    ReenviarLogo();
                }
            }
            else if (GrupoServidor.Sim(r, "fora")) { SairDoGrupoLocal("O grupo acabou (o líder encerrou ou ficou 6 h parado)."); return; }
            else grupoErro = GrupoServidor.Txt(r, "erro") ?? "Não deu para atualizar agora. Tento de novo em 2 minutos.";
            DesenharGrupo();
        }
        void SairDoGrupoLocal(string motivo)
        {
            grupoAtual = null; grupoEstado = null; GrupoServidor.Atual = null; if (grupoTimer != null) grupoTimer.Stop();
            Log("grupo: saiu (" + motivo + ")");
            if (janelaGrupo != null && janelaGrupo.W.IsVisible) { janelaGrupo.W.Hide(); P.Abertas.Remove("grupo"); }
            if (motivo != null) MessageBox.Show(janelas["hunt"].W, motivo, Info.Nome, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---------------- botão Grupo ----------------
        void AbrirGrupo()
        {
            if (EmGrupo) { MostrarJanelaGrupo(); return; }
            var hunt = janelas["hunt"].W;
            var w = Dialogo("Caçada em grupo", hunt, 300);
            var st = new StackPanel();
            st.Children.Add(Paragrafo("Juntem até 6 analyzers e vejam o loot, os supplies e o balanço do grupo e de cada um."));
            st.Children.Add(new TextBlock { Text = "Nome do seu personagem", Foreground = Cor("#8B909A"), FontSize = 10.5, Margin = new Thickness(0, 2, 0, 3) });
            var nome = Caixa(File.Exists(GrupoServidor.ArqNome) ? File.ReadAllText(GrupoServidor.ArqNome).Trim() : "");
            st.Children.Add(nome);
            st.Children.Add(Separador());
            st.Children.Add(Titulo("Entrar num grupo"));
            var linhaE = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            linhaE.ColumnDefinitions.Add(new ColumnDefinition()); linhaE.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cod = Caixa(""); cod.CharacterCasing = CharacterCasing.Upper; cod.MaxLength = 7; cod.Margin = new Thickness(0, 0, 6, 0);
            var entrar = Botao("Entrar"); Grid.SetColumn(entrar, 1);
            linhaE.Children.Add(cod); linhaE.Children.Add(entrar); st.Children.Add(linhaE);
            st.Children.Add(Paragrafo("Peça o código de 6 letras para o líder.", "#8B909A"));
            st.Children.Add(Separador());
            st.Children.Add(Titulo("Criar um grupo (líder)"));
            var temAcesso = GrupoServidor.TokenAcesso != null;
#if TESTE
            if (TutorialTeste.SemChaveSalva) temAcesso = false;
#endif
            var chave = Caixa(""); chave.CharacterCasing = CharacterCasing.Upper;
            var criar = Botao(temAcesso ? "Criar grupo" : "Ativar e criar");
            if (!temAcesso)
            {
                st.Children.Add(Paragrafo("Criar grupos é liberado pela equipe da Bloodpedia. Se você recebeu uma chave de líder, digite abaixo.", "#8B909A"));
                st.Children.Add(chave);
            }
            criar.HorizontalAlignment = HorizontalAlignment.Right; criar.Margin = new Thickness(0, 6, 0, 0); st.Children.Add(criar);
            var msg = Paragrafo("", "#FF8A8A"); msg.Visibility = Visibility.Collapsed; msg.Margin = new Thickness(0, 8, 0, 4); st.Children.Add(msg);
            st.Children.Add(Separador());
            st.Children.Add(Paragrafo("No grupo, o seu nome e os números da sua caçada ficam visíveis para quem está nele, e são apagados do servidor 6 horas depois da última atualização.", "#7D828C"));
            st.Children.Add(Link("Política de privacidade", Licenca.PaginaPrivacidade));
            w.Content = st;

            Action<string> erro = t => { msg.Text = t; msg.Foreground = Cor("#FF8A8A"); msg.Visibility = Visibility.Visible; entrar.IsEnabled = criar.IsEnabled = true; };
            Func<string> nomeOk = () => { var n = nome.Text.Trim(); if (n.Length < 2) { erro("Digite o nome do seu personagem."); return null; } try { File.WriteAllText(GrupoServidor.ArqNome, n); } catch { } return n; };
            Action<Dictionary<string, object>, string, string> entrou = (r, codigo, n) =>
            {
                grupoAtual = new[] { codigo, GrupoServidor.Txt(r, "membro"), GrupoServidor.Txt(r, "segredo"), n };
                GrupoServidor.Atual = grupoAtual; grupoEstado = GrupoServidor.Obj(r, "estado");
                grupoEpoca = GrupoServidor.Num(grupoEstado, "epoca"); NovaBaseGrupo(null);   // conta a partir de quando entrou
                Log("grupo: entrou em " + codigo); w.Close();
                grupoTimer.Start(); EnviarGrupo(false); MostrarJanelaGrupo();
            };
            entrar.Click += (s, e) =>
            {
                var n = nomeOk(); if (n == null) return;
                var c = new string(cod.Text.Where(char.IsLetterOrDigit).ToArray());
                if (c.Length != 6) { erro("O código tem 6 letras e números."); return; }
                entrar.IsEnabled = criar.IsEnabled = false; msg.Foreground = Cor("#8B909A"); msg.Text = "Entrando…"; msg.Visibility = Visibility.Visible;
                new Thread(() =>
                {
                    int stt; var r = GrupoServidor.Post("grupo", new Dictionary<string, object> { { "acao", "entrar" }, { "codigo", c }, { "nome", n } }, out stt);
                    w.Dispatcher.BeginInvoke(new Action(() => { if (stt == 200) entrou(r, c, n); else erro(GrupoServidor.Txt(r, "erro") ?? "Não deu para entrar."); }));
                }) { IsBackground = true }.Start();
            };
            criar.Click += (s, e) =>
            {
                var n = nomeOk(); if (n == null) return;
                var k = chave.Text.Trim();
                if (GrupoServidor.TokenAcesso == null && k.Length < 8) { erro("Digite a sua chave de líder."); return; }
                entrar.IsEnabled = criar.IsEnabled = false; msg.Foreground = Cor("#8B909A"); msg.Text = "Criando o grupo…"; msg.Visibility = Visibility.Visible;
                new Thread(() =>
                {
                    int stt; Dictionary<string, object> r;
                    if (GrupoServidor.TokenAcesso == null)
                    {
                        r = GrupoServidor.Post("acesso", new Dictionary<string, object> { { "chave", k }, { "maquina", Licenca.MaquinaPara("grupo") } }, out stt);
                        if (stt != 200) { w.Dispatcher.BeginInvoke(new Action(() => erro(GrupoServidor.Txt(r, "erro") ?? "Não deu para ativar a chave."))); return; }
                        GrupoServidor.TokenAcesso = GrupoServidor.Txt(r, "token");
                    }
                    r = GrupoServidor.Post("grupo", new Dictionary<string, object> { { "acao", "criar" }, { "token", GrupoServidor.TokenAcesso }, { "nome", n } }, out stt);
                    if (stt == 403 && GrupoServidor.Sim(r, "semAcesso")) GrupoServidor.TokenAcesso = null;   // revogada
                    w.Dispatcher.BeginInvoke(new Action(() => { if (stt == 200) entrou(r, GrupoServidor.Txt(r, "codigo"), n); else erro(GrupoServidor.Txt(r, "erro") ?? "Não deu para criar o grupo."); }));
                }) { IsBackground = true }.Start();
            };
            w.Show(); nome.Focus();
        }
        TextBox Caixa(string t) { return new TextBox { Text = t, FontSize = 13, Padding = new Thickness(6, 3, 6, 3), Background = Cor("#101216"), Foreground = Brushes.White, BorderBrush = Cor("#3A3F49"), CaretBrush = Brushes.White }; }
        static Button Botao(string t) { return new Button { Content = t, Style = (Style)Application.Current.Resources["BotaoAtalho"], Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0) }; }

        // ---------------- janela Party Hunt ----------------
        void MostrarJanelaGrupo()
        {
            if (janelaGrupo == null)
            {
                grupoCorpo = new StackPanel();
                var w = new Window { Title = "Party Hunt", Width = 236, Content = grupoCorpo, Tag = Cor("#C25CE0"), Uid = "", Topmost = P.PorCima };
                w.Style = (Style)Application.Current.Resources["JanelaAnalyzer"];
                double[] pos; w.WindowStartupLocation = WindowStartupLocation.Manual;
                if (P.Posicoes.TryGetValue("grupo", out pos)) { w.Left = pos[0]; w.Top = pos[1]; } else { var h = janelas["hunt"].W; w.Left = h.Left - 244; w.Top = h.Top; }
                janelaGrupo = new Janela { Chave = "grupo", W = w, Corpo = grupoCorpo };
                w.Loaded += (s, e) =>
                {
                    w.ApplyTemplate();
                    ((FrameworkElement)w.Template.FindName("PART_Titulo", w)).MouseLeftButtonDown += (s2, e2) => { if (e2.ButtonState == MouseButtonState.Pressed) { w.DragMove(); SalvarPosicoes(); } };
                    var corpoP = (FrameworkElement)w.Template.FindName("PART_Corpo", w);
                    ((Button)w.Template.FindName("PART_Recolher", w)).Click += (s2, e2) => corpoP.Visibility = corpoP.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                    ((Button)w.Template.FindName("PART_Fechar", w)).Click += (s2, e2) => { w.Hide(); P.Abertas.Remove("grupo"); P.Salvar(); };
                };
                janelas["grupo"] = janelaGrupo;
            }
            DesenharGrupo();
            if (!janelaGrupo.W.IsVisible) { janelaGrupo.W.Show(); if (trava != null) trava.Aplicar(janelaGrupo.W); P.Abertas.Add("grupo"); P.Salvar(); }
            else janelaGrupo.W.Activate();
        }

        static TextBlock Linha(string t, string cor, double tam = 11.5, FontWeight? peso = null) { return new TextBlock { Text = t, Foreground = Cor(cor), FontSize = tam, FontWeight = peso ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis }; }
        static Grid ParValor(string rotulo, string valor, string cor)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.Children.Add(Linha(rotulo, "#9AA0AA"));
            var v = Linha(valor, cor, 11.5, FontWeights.SemiBold); v.HorizontalAlignment = HorizontalAlignment.Right; g.Children.Add(v);
            return g;
        }

        void DesenharGrupo()
        {
            if (grupoCorpo == null || grupoAtual == null) return;
            var c = grupoCorpo; c.Children.Clear();
            var g = grupoEstado; var eu = grupoAtual[1];
            // cabeçalho: código para passar aos outros
            var cab = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            var tc = new TextBlock(); tc.Inlines.Add(new System.Windows.Documents.Run("Código ") { Foreground = Cor("#9AA0AA") });
            tc.Inlines.Add(new System.Windows.Documents.Run(grupoAtual[0]) { Foreground = Cor("#E9EBEE"), FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas"), FontSize = 13 });
            tc.VerticalAlignment = VerticalAlignment.Center; cab.Children.Add(tc);
            var copiar = new Button { Style = (Style)Application.Current.Resources["BotaoTitulo"], Content = "", ToolTip = "Copiar o código", HorizontalAlignment = HorizontalAlignment.Right };
            copiar.Click += (s, e) => { try { Clipboard.SetText(grupoAtual[0]); copiar.Content = ""; } catch { } };
            cab.Children.Add(copiar); c.Children.Add(cab);
            var atual = grupoUltimoEnvio == DateTime.MinValue ? "enviando…" : "atualizado há " + Math.Max(0, (int)(DateTime.Now - grupoUltimoEnvio).TotalMinutes) + " min";
            c.Children.Add(Linha(grupoErro ?? (atual + " · a cada 2 min"), grupoErro != null ? "#FF8A8A" : "#626772", 10.5));
            c.Children.Add(new Border { Style = (Style)Application.Current.Resources["Separador"], Margin = new Thickness(0, 5, 0, 5) });

            var membros = GrupoServidor.Lista(g, "membros").ToList();
            if (g == null || membros.Count == 0) { c.Children.Add(Paragrafo("Esperando os números do grupo…", "#8B909A")); Rodape(c, false); return; }
            long criado = GrupoServidor.Num(g, "criado"), agora = GrupoServidor.Num(g, "agora");
            Func<Dictionary<string, object>, string, long> n = (m, k) => GrupoServidor.Num(GrupoServidor.Obj(m, "dados"), k);
            var lider = membros.FirstOrDefault(m => GrupoServidor.Sim(m, "lider"));
            long loot = membros.Sum(m => n(m, "loot")), sup = membros.Sum(m => n(m, "supplies")), bal = loot - sup;
            double horas = Math.Max((agora - criado) / 3600000.0, 1.0 / 60);
            var dur = TimeSpan.FromMilliseconds(Math.Max(0, agora - criado));
            c.Children.Add(ParValor("Sessão:", string.Format("{0:00}:{1:00}h", (int)dur.TotalHours, dur.Minutes), "#E9EBEE"));
            c.Children.Add(ParValor("Loot:", Inteiro(loot), "#F2B544"));
            c.Children.Add(ParValor("Supplies:", Inteiro(sup), "#E9EBEE"));
            c.Children.Add(ParValor("Balanço:", Inteiro(bal), bal >= 0 ? "#35D07F" : "#FF5A64"));
            c.Children.Add(ParValor("Balanço/h:", Inteiro((long)(bal / horas)), bal >= 0 ? "#35D07F" : "#FF5A64"));
            c.Children.Add(new Border { Style = (Style)Application.Current.Resources["Separador"], Margin = new Thickness(0, 5, 0, 5) });

            // cada um: balanço (loot que tem − supplies que gastou), supplies, dano e cura
            foreach (var m in membros)
            {
                bool eLider = GrupoServidor.Sim(m, "lider"), semDados = GrupoServidor.Obj(m, "dados") == null;
                long at = GrupoServidor.Num(m, "atualizado"); bool parado = !semDados && agora - at > 5 * 60000;
                long sm = n(m, "supplies"), lm = n(m, "loot"), bm = lm - sm;
                // cartão da pessoa: nome em cima e três números com etiqueta (ícone + nome), para ler sem precisar adivinhar
                var cartao = new Border { CornerRadius = new CornerRadius(7), Background = Cor("#1E2128"), BorderBrush = Cor(eLider ? "#4A4128" : "#2A2E35"), BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 4, 7, 5), Margin = new Thickness(0, 0, 0, 4), Opacity = semDados || parado ? 0.5 : 1 };
                var bloco = new StackPanel(); cartao.Child = bloco;
                var nome = Linha((eLider ? "\U0001F451 " : "") + GrupoServidor.Txt(m, "nome") + (GrupoServidor.Txt(m, "id") == eu ? " (você)" : ""), eLider ? "#F2C94C" : "#E9EBEE", 11.5, FontWeights.SemiBold);
                if (parado) nome.ToolTip = "Sem atualização há mais de 5 minutos";
                bloco.Children.Add(nome);
                if (semDados) bloco.Children.Add(Linha("esperando o primeiro envio…", "#626772", 10.5));
                else
                {
                    var l = new Grid { Margin = new Thickness(0, 3, 0, 0) };
                    for (int i = 0; i < 3; i++) l.ColumnDefinitions.Add(new ColumnDefinition());
                    Action<int, string, string, string, string, string> col = (i, ic, rotulo, v, cor, dica) =>
                    {
                        var s = new StackPanel { ToolTip = dica };
                        var et = new TextBlock { FontSize = 9.5 };
                        et.Inlines.Add(new System.Windows.Documents.Run(ic + " ") { Foreground = Cor(cor), FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol") });
                        et.Inlines.Add(new System.Windows.Documents.Run(rotulo) { Foreground = Cor("#7D828C") });
                        s.Children.Add(et);
                        s.Children.Add(new TextBlock { Text = v, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Cor(cor) });
                        Grid.SetColumn(s, i); l.Children.Add(s);
                    };
                    col(0, "\U0001F4B0", "Balanço", Num(bm), bm >= 0 ? "#35D07F" : "#FF5A64", "Balanço: " + Inteiro(bm) + " gp\n(" +Inteiro(lm) + " gp de loot − " + Inteiro(sm) + " gp de supplies)");
                    col(1, "⚔", "Dano", Num(n(m, "dano")), "#FF9F5A", "Dano causado: " + Inteiro(n(m, "dano")));
                    col(2, "✚", "Cura", Num(n(m, "cura")), "#5B9BFF", "Vida recuperada: " + Inteiro(n(m, "cura")));
                    bloco.Children.Add(l);
                }
                c.Children.Add(cartao);
            }

            // divisão: todos terminam com o mesmo lucro; o acerto passa pelo líder (quem ficou com mais paga a diferença)
            var comDados = membros.Where(m => GrupoServidor.Obj(m, "dados") != null).ToList();
            if (comDados.Count > 1 && lider != null)
            {
                long parte = bal / comDados.Count;
                c.Children.Add(new Border { Style = (Style)Application.Current.Resources["Separador"], Margin = new Thickness(0, 3, 0, 5) });
                c.Children.Add(ParValor("Lucro de cada um:", Inteiro(parte), parte >= 0 ? "#35D07F" : "#FF5A64"));
                c.Children.Add(Linha("Para dividir igual:", "#7D828C", 10.5));
                foreach (var m in comDados.Where(x => !GrupoServidor.Sim(x, "lider")))
                {
                    long recebe = parte - (n(m, "loot") - n(m, "supplies"));   // o que falta (ou sobra) para a pessoa ficar com a parte dela
                    var t = new TextBlock { FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
                    if (recebe >= 0) { t.Inlines.Add(new System.Windows.Documents.Run("Líder → ") { Foreground = Cor("#7D828C") }); t.Inlines.Add(new System.Windows.Documents.Run(GrupoServidor.Txt(m, "nome")) { Foreground = Cor("#C9CDD4") }); }
                    else { t.Inlines.Add(new System.Windows.Documents.Run(GrupoServidor.Txt(m, "nome")) { Foreground = Cor("#C9CDD4") }); t.Inlines.Add(new System.Windows.Documents.Run(" → líder") { Foreground = Cor("#7D828C") }); }
                    t.Inlines.Add(new System.Windows.Documents.Run(": " + Inteiro(Math.Abs(recebe))) { Foreground = Cor("#F2B544"), FontWeight = FontWeights.SemiBold });
                    c.Children.Add(t);
                }
            }
            Rodape(c, lider != null && GrupoServidor.Txt(lider, "id") == eu);
        }

        void Rodape(StackPanel c, bool souLider)
        {
            var lin = new UniformGrid2(); lin.Margin = new Thickness(0, 8, -3, -3);
            var enviar = new Button { Content = "Enviar agora", Style = (Style)Application.Current.Resources["BotaoAtalho"], Padding = new Thickness(1, 0, 1, 0) };
            var sair = new Button { Content = souLider ? "Encerrar" : "Sair", Style = (Style)Application.Current.Resources["BotaoAtalho"], Padding = new Thickness(1, 0, 1, 0), ToolTip = souLider ? "Encerrar o grupo para todos" : "Sair do grupo" };
            enviar.Click += (s, e) => { enviar.IsEnabled = false; EnviarGrupo(false); };
            sair.Click += (s, e) =>
            {
                if (MessageBox.Show(janelaGrupo.W, souLider ? "Encerrar o grupo para todos?" : "Sair do grupo?", Info.Nome, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var g = grupoAtual; SairDoGrupoLocal(null);
                new Thread(() => { int st; GrupoServidor.Post("grupo", new Dictionary<string, object> { { "acao", "sair" }, { "codigo", g[0] }, { "membro", g[1] }, { "segredo", g[2] } }, out st); }) { IsBackground = true }.Start();
            };
            lin.Children.Add(enviar);
            if (souLider)
            {
                // zerar a sessão do grupo: só o líder (o servidor também confere)
                var zerar = new Button { Content = "Zerar", Style = (Style)Application.Current.Resources["BotaoAtalho"], Padding = new Thickness(1, 0, 1, 0), ToolTip = "Zerar a sessão do grupo para todos (só o líder)" };
                zerar.Click += (s, e) =>
                {
                    if (MessageBox.Show(janelaGrupo.W, "Zerar a sessão do grupo para todos?\n\nO loot, os supplies, o dano e a cura do grupo voltam a zero e a contagem recomeça agora. A sessão de cada um no Hunt Analyzer não muda.", Info.Nome, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    zerar.IsEnabled = false;
                    var g = grupoAtual;
                    new Thread(() =>
                    {
                        int st; var r = GrupoServidor.Post("grupo", new Dictionary<string, object> { { "acao", "zerar" }, { "codigo", g[0] }, { "membro", g[1] }, { "segredo", g[2] } }, out st);
                        janelas["hunt"].W.Dispatcher.BeginInvoke(new Action(() => RespostaGrupo(st, r)));
                    }) { IsBackground = true }.Start();
                };
                lin.Children.Add(zerar);
            }
            lin.Children.Add(sair); c.Children.Add(lin);
        }
    }

#if TESTE
    partial class Programa
    {
        static void Foto(Window w, string arq)
        {
            w.Left = -10000; w.ShowInTaskbar = false; if (!w.IsVisible) w.Show();
            w.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); w.UpdateLayout();
            var raiz = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); rtb.Render(raiz);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var f = File.Create(arq)) enc.Save(f);
        }
        // a janela em 2x, sobre um fundo escuro com margem (print para divulgação)
        static void Foto2x(Window w, string arq)
        {
            w.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle); w.UpdateLayout();
            var raiz = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
            int lw = (int)w.ActualWidth * 2, lh = (int)w.ActualHeight * 2, m = 48;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(lw, lh, 192, 192, PixelFormats.Pbgra32); rtb.Render(raiz);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) { dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0E, 0x0F, 0x12)), null, new Rect(0, 0, lw + 2 * m, lh + 2 * m)); dc.DrawImage(rtb, new Rect(m, m, lw, lh)); }
            var fimb = new System.Windows.Media.Imaging.RenderTargetBitmap(lw + 2 * m, lh + 2 * m, 96, 96, PixelFormats.Pbgra32); fimb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(fimb));
            using (var fs = File.Create(arq)) enc.Save(fs);
        }
        public void TestarGrupo(string pasta)
        {
            Carregar(); P.PorCima = false;
            foreach (var d in Definicoes) Criar(d);
            janelas["hunt"].W.Left = -10000; janelas["hunt"].W.Show();
            long agora = 1800000000000, criado = agora - 47 * 60000;
            var json = "{\"codigo\":\"KX7P2M\",\"criado\":" + criado + ",\"agora\":" + agora + ",\"lider\":\"aaaaaaaa\",\"epoca\":0,\"membros\":[" +
                "{\"id\":\"aaaaaaaa\",\"nome\":\"Eduzinho\",\"lider\":true,\"atualizado\":" + (agora - 1 * 60000) + ",\"dados\":{\"loot\":182400,\"supplies\":21300,\"dano\":52800,\"cura\":4100}}" + "," +
                "{\"id\":\"bbbbbbbb\",\"nome\":\"Arqueira Lia\",\"lider\":false,\"atualizado\":" + (agora - 1 * 60000) + ",\"dados\":{\"loot\":0,\"supplies\":18700,\"dano\":41200,\"cura\":9850}}" + "," +
                "{\"id\":\"cccccccc\",\"nome\":\"Xamã Torr\",\"lider\":false,\"atualizado\":" + (agora - 2 * 60000) + ",\"dados\":{\"loot\":0,\"supplies\":15900,\"dano\":36700,\"cura\":12300}}" + "]}";
            grupoAtual = new[] { "KX7P2M", "aaaaaaaa", "x", "Eduzinho" };
            grupoEstado = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            grupoUltimoEnvio = DateTime.Now.AddMinutes(-1);
            MostrarJanelaGrupo(); Foto(janelaGrupo.W, Path.Combine(pasta, "party.png"));
            Foto2x(janelaGrupo.W, Path.Combine(pasta, "party-2x.png"));
            Foto(janelas["hunt"].W, Path.Combine(pasta, "hunt.png"));
            grupoAtual = null; AbrirGrupo();
            var dlg = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.Title == "Caçada em grupo");
            if (dlg != null) Foto(dlg, Path.Combine(pasta, "entrar.png"));
        }
    }
#endif

    // grade simples de colunas iguais (os botões do rodapé)
    class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid2() { Rows = 1; } }
}
