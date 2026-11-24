// Analyzer Bloodpedia (antes BloodStone Hunt Analyzer) — reconstrução do BloodstoneAnalyzer v2.8 para o cliente 1.49.
// Lê (somente leitura) a memória do client.exe: XP, canal "Pilhagem" (kills e loot) e canal "Servidor" (dano).
// Nada é escrito no jogo e nada é enviado para a internet.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BloodstoneHunt
{
    // ---------------- endereços do cliente (ajustáveis em offsets.ini) ----------------
    class Offsets
    {
        public long Experiencia = 0x368280;   // u64: XP total do personagem
        public long Canais = 0x375048;        // std::vector<{canal*, ?}> begin/end
        public int CanalNome = 0x04;          // QString
        public int CanalMapa = 0x0C;          // std::deque: _Map, _Mapsize, _Myoff, _Mysize
        public int MsgHora = 0x08;            // QString "16:45"
        public int MsgTexto = 0x10;           // QString com o texto (pode ter <font>)
        public string Versao = "1.49";
        // vida/mana: [base+EstadoJogo] +0x1F0 -> jogo; jogo+0x1F0 -> std::map de jogadores (head, size); nó+0x78 = nível, vida, vidaMax, mana, manaMax
        public long EstadoJogo = 0x3779C8;
        public int Jogo = 0x1F0, MapaJogadores = 0x1F0, Status = 0x78;
        // itens: tabela de tipos (quantidade, ponteiro), marca (vtable) dos objetos de item, tipo em +0x10 e quantidade (u16) em +0x14
        public long TabelaItens = 0x3680C4, VtableItem = 0x27A624;
        public int ItemTipo = 0x10, ItemQtd = 0x14, ItemTotal = 0xA8;   // ItemTotal: total que o personagem carrega, no tipo do item

        public static Offsets Carregar(string arquivo)
        {
            var o = new Offsets();
            if (!File.Exists(arquivo)) { o.Salvar(arquivo); return o; }
            foreach (var l in File.ReadAllLines(arquivo))
            {
                var p = l.Split(new[] { '=' }, 2); if (p.Length < 2 || l.TrimStart().StartsWith(";")) continue;
                string k = p[0].Trim(), v = p[1].Trim();
                long n; bool hex = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                if (!long.TryParse(hex ? v.Substring(2) : v, hex ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { if (k == "versao") o.Versao = v; continue; }
                switch (k)
                {
                    case "experiencia": o.Experiencia = n; break;
                    case "canais": o.Canais = n; break;
                    case "canal_nome": o.CanalNome = (int)n; break;
                    case "canal_mapa": o.CanalMapa = (int)n; break;
                    case "msg_hora": o.MsgHora = (int)n; break;
                    case "msg_texto": o.MsgTexto = (int)n; break;
                    case "estado_jogo": o.EstadoJogo = n; break;
                    case "jogo": o.Jogo = (int)n; break;
                    case "mapa_jogadores": o.MapaJogadores = (int)n; break;
                    case "status": o.Status = (int)n; break;
                    case "tabela_itens": o.TabelaItens = n; break;
                    case "vtable_item": o.VtableItem = n; break;
                    case "item_tipo": o.ItemTipo = (int)n; break;
                    case "item_qtd": o.ItemQtd = (int)n; break;
                    case "item_total": o.ItemTotal = (int)n; break;
                }
            }
            return o;
        }
        public void Salvar(string arquivo)
        {
            try
            {
                File.WriteAllText(arquivo, string.Format(
                    "; Endereços do client.exe (relativos ao início do módulo). Mudam quando o jogo atualiza.\r\n" +
                    "versao = {0}\r\nexperiencia = 0x{1:X}\r\ncanais = 0x{2:X}\r\ncanal_nome = 0x{3:X}\r\ncanal_mapa = 0x{4:X}\r\nmsg_hora = 0x{5:X}\r\nmsg_texto = 0x{6:X}\r\n" +
                    "estado_jogo = 0x{7:X}\r\njogo = 0x{8:X}\r\nmapa_jogadores = 0x{9:X}\r\nstatus = 0x{10:X}\r\ntabela_itens = 0x{11:X}\r\nvtable_item = 0x{12:X}\r\nitem_tipo = 0x{13:X}\r\nitem_qtd = 0x{14:X}\r\nitem_total = 0x{15:X}\r\n",
                    Versao, Experiencia, Canais, CanalNome, CanalMapa, MsgHora, MsgTexto, EstadoJogo, Jogo, MapaJogadores, Status, TabelaItens, VtableItem, ItemTipo, ItemQtd, ItemTotal));
            }
            catch { }
        }
    }

    // ---------------- leitura de memória (somente leitura) ----------------
    class Memoria : IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int acesso, bool herdar, int pid);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr h, IntPtr end, byte[] buf, int n, out int lidos);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        const int PROCESS_VM_READ = 0x0010, PROCESS_QUERY_INFORMATION = 0x0400;

        public IntPtr Handle; public int Pid; public long Base; public Process Processo;

        public static Memoria Abrir(Process p, out string erro)
        {
            erro = null;
            var h = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, p.Id);
            if (h == IntPtr.Zero)
            {
                int e = Marshal.GetLastWin32Error();
                erro = e == 5 ? "Sem permissão: abra o analyzer como administrador" : "Não foi possível abrir o jogo (erro " + e + ")";
                return null;
            }
            long b;
            try { b = p.MainModule.BaseAddress.ToInt64(); }
            catch (Exception ex) { CloseHandle(h); erro = "Não foi possível localizar o client.exe: " + ex.Message; return null; }
            return new Memoria { Handle = h, Pid = p.Id, Base = b, Processo = p };
        }
        public byte[] Ler(long end, int n)
        {
            if (end < 0x10000 || end > 0x7FFEFFFF || n <= 0) return null;
            var b = new byte[n]; int r;
            return ReadProcessMemory(Handle, (IntPtr)end, b, n, out r) && r == n ? b : null;
        }
        public bool U32(long end, out uint v) { var b = Ler(end, 4); v = b == null ? 0 : BitConverter.ToUInt32(b, 0); return b != null; }
        public uint U32(long end) { uint v; U32(end, out v); return v; }
        public bool U64(long end, out ulong v) { var b = Ler(end, 8); v = b == null ? 0 : BitConverter.ToUInt64(b, 0); return b != null; }
        // QString do Qt5 (32 bits): ponteiro para QArrayData { ref, size, alloc, offset }
        public string QString(long campo, int max)
        {
            uint d; if (!U32(campo, out d)) return null;
            var h = Ler(d, 16); if (h == null) return null;
            int tam = BitConverter.ToInt32(h, 4), off = BitConverter.ToInt32(h, 12);
            if (tam < 0 || tam > max || off < 0 || off > 4096) return null;
            if (tam == 0) return "";
            var t = Ler(d + off, tam * 2); return t == null ? null : Encoding.Unicode.GetString(t);
        }
        [DllImport("kernel32.dll")] static extern int VirtualQueryEx(IntPtr h, IntPtr end, out MBI m, int tam);
        [StructLayout(LayoutKind.Sequential)] struct MBI { public IntPtr Base, AllocBase; public uint AllocProt; public IntPtr Size; public uint State, Protect, Type; }
        // percorre a memória privada e gravável do jogo e chama "achou" para cada u32 alinhado presente em "valores"
        public void Varrer(HashSet<uint> valores, Action<long, uint> achou)
        {
            long a = 0x10000; MBI m; var buf = new byte[4 * 1024 * 1024];
            while (a < 0x7FFE0000 && VirtualQueryEx(Handle, (IntPtr)a, out m, Marshal.SizeOf(typeof(MBI))) != 0)
            {
                long ini = m.Base.ToInt64(), tam = m.Size.ToInt64();
                // MEM_COMMIT, MEM_PRIVATE, leitura e escrita, sem página de guarda
                if (m.State == 0x1000 && m.Type == 0x20000 && (m.Protect & 0x100) == 0 && (m.Protect & 0x44) != 0)
                    for (long p = ini; p < ini + tam; p += buf.Length)
                    {
                        int n = (int)Math.Min(buf.Length, ini + tam - p), r;
                        if (!ReadProcessMemory(Handle, (IntPtr)p, buf, n, out r)) continue;
                        for (int i = 0; i + 4 <= r; i += 4) { uint v = BitConverter.ToUInt32(buf, i); if (valores.Contains(v)) achou(p + i, v); }
                    }
                a = ini + tam;
            }
        }
        public bool Vivo { get { try { return !Processo.HasExited; } catch { return false; } } }
        public void Dispose() { if (Handle != IntPtr.Zero) CloseHandle(Handle); Handle = IntPtr.Zero; }
    }

    class Mensagem { public uint Ptr; public string Hora, Texto; public string Chave { get { return Ptr.ToString("X") + "|" + Texto; } } }

    // ---------------- leitor do jogo ----------------
    class LeitorJogo
    {
        readonly Offsets O; public Memoria M;
        public LeitorJogo(Offsets o) { O = o; }

        public bool LerXp(out long xp)
        {
            ulong v; xp = 0;
            if (M == null || !M.U64(M.Base + O.Experiencia, out v) || v > 1000000000000UL) return false;
            xp = (long)v; return true;
        }

        // canais de chat: nome -> endereço do objeto do canal
        public Dictionary<string, uint> Canais()
        {
            var r = new Dictionary<string, uint>();
            uint ini, fim;
            if (!M.U32(M.Base + O.Canais, out ini) || !M.U32(M.Base + O.Canais + 4, out fim)) return r;
            if (ini == 0 || fim < ini || fim - ini > 0x800 || (fim - ini) % 8 != 0) return r;
            var b = M.Ler(ini, (int)(fim - ini)); if (b == null) return r;
            for (int i = 0; i < b.Length; i += 8)
            {
                uint c = BitConverter.ToUInt32(b, i); if (c < 0x10000) continue;
                var nome = M.QString(c + O.CanalNome, 64);
                if (!string.IsNullOrEmpty(nome) && !r.ContainsKey(nome.Trim())) r[nome.Trim()] = c;
            }
            return r;
        }

        // mensagens de um canal: std::deque<shared_ptr<Mensagem>> com 2 elementos de 8 bytes por bloco
        public List<Mensagem> Mensagens(uint canal, int ultimas)
        {
            var lista = new List<Mensagem>();
            var h = M.Ler(canal + O.CanalMapa, 16); if (h == null) return lista;
            uint mapa = BitConverter.ToUInt32(h, 0), tamMapa = BitConverter.ToUInt32(h, 4), off = BitConverter.ToUInt32(h, 8), qtd = BitConverter.ToUInt32(h, 12);
            if (qtd == 0) return lista;
            if (mapa < 0x10000 || tamMapa == 0 || tamMapa > 0x10000 || (tamMapa & (tamMapa - 1)) != 0 || qtd > 100000) return lista;
            var blocos = M.Ler(mapa, (int)tamMapa * 4); if (blocos == null) return lista;
            uint ini = qtd > ultimas ? qtd - (uint)ultimas : 0;
            for (uint i = ini; i < qtd; i++)
            {
                uint k = off + i;
                uint bloco = BitConverter.ToUInt32(blocos, (int)(((k >> 1) & (tamMapa - 1)) * 4));
                uint p; if (!M.U32(bloco + (k & 1) * 8, out p) || p < 0x10000) continue;
                var texto = M.QString(p + O.MsgTexto, 4000); if (texto == null) continue;
                lista.Add(new Mensagem { Ptr = p, Hora = M.QString(p + O.MsgHora, 16) ?? "", Texto = texto });
            }
            return lista;
        }

        // ---- vida e mana do personagem ----
        uint noJogador;
        public StatusJogador LerStatus()
        {
            uint est, jogo, head, root; uint tam;
            if (!M.U32(M.Base + O.EstadoJogo, out est) || !M.U32(est + O.Jogo, out jogo)) return null;
            if (!M.U32(jogo + O.MapaJogadores, out head) || !M.U32(jogo + O.MapaJogadores + 4, out tam) || tam == 0 || tam > 64) return null;
            if (!M.U32(head + 4, out root) || root < 0x10000) return null;
            // com mais de um jogador no mapa, mantém o nó escolhido antes enquanto ele existir
            var nos = new List<uint>(); Coletar(root, head, nos, 0);
            uint no = nos.Contains(noJogador) ? noJogador : root;
            var b = M.Ler(no + O.Status, 20); if (b == null) return null;
            var s = new StatusJogador { Nivel = BitConverter.ToInt32(b, 0), Vida = BitConverter.ToInt32(b, 4), VidaMax = BitConverter.ToInt32(b, 8), Mana = BitConverter.ToInt32(b, 12), ManaMax = BitConverter.ToInt32(b, 16) };
            if (s.Nivel <= 0 || s.Nivel > 5000 || s.VidaMax <= 0 || s.Vida < 0 || s.Vida > s.VidaMax || s.ManaMax < 0 || s.Mana < 0 || s.Mana > s.ManaMax) return null;
            noJogador = no; return s;
        }
        void Coletar(uint no, uint head, List<uint> lista, int prof)
        {
            if (no == head || no < 0x10000 || prof > 12 || lista.Count > 64) return;
            lista.Add(no); uint e, d;
            if (M.U32(no, out e)) Coletar(e, head, lista, prof + 1);
            if (M.U32(no + 8, out d)) Coletar(d, head, lista, prof + 1);
        }

        // ---- itens de supply: objetos de item cujo tipo é um supply ----
        public Dictionary<uint, int> TiposSupply(IEnumerable<int> ids)   // ponteiro do tipo -> id do item
        {
            var r = new Dictionary<uint, int>(); uint qtd, tab;
            if (!M.U32(M.Base + O.TabelaItens, out qtd) || !M.U32(M.Base + O.TabelaItens + 4, out tab)) return r;
            foreach (var id in ids) { uint tp; if (id > 0 && id < qtd && M.U32(tab + (uint)id * 4, out tp) && tp > 0x10000) r[tp] = id; }
            return r;
        }
        // lê um objeto de item; devolve a quantidade ou -1 se não for mais um item daquele tipo
        public int QtdItem(uint obj, uint tipo)
        {
            var b = M.Ler(obj, O.ItemQtd + 2); if (b == null) return -1;
            if (BitConverter.ToUInt32(b, 0) != (uint)(M.Base + O.VtableItem) || BitConverter.ToUInt32(b, O.ItemTipo) != tipo) return -1;
            return BitConverter.ToUInt16(b, O.ItemQtd);
        }
        public Dictionary<uint, uint> AcharItens(Dictionary<uint, int> tipos)   // objeto -> ponteiro do tipo
        {
            var r = new Dictionary<uint, uint>();
            M.Varrer(new HashSet<uint>(tipos.Keys), (end, v) => { uint obj = (uint)(end - O.ItemTipo); if (QtdItem(obj, v) > 0) r[obj] = v; });
            return r;
        }
    }

    class StatusJogador { public int Nivel, Vida, VidaMax, Mana, ManaMax; }

    // acompanha um canal e devolve só as mensagens novas (maior sobreposição entre a leitura anterior e a atual)
    class Acompanhante
    {
        List<string> anterior;
        public List<Mensagem> Novas(List<Mensagem> atual)
        {
            var chaves = atual.Select(m => m.Chave).ToList();
            if (anterior == null) { anterior = chaves; return new List<Mensagem>(); }
            int melhor = 0;
            for (int n = Math.Min(anterior.Count, chaves.Count); n > 0; n--)
            {
                bool ok = true;
                for (int i = 0; i < n && ok; i++) ok = anterior[anterior.Count - n + i] == chaves[i];
                if (ok) { melhor = n; break; }
            }
            // se nada bate mas a lista não mudou de tamanho, é provável que o canal tenha sido limpo e refeito
            var novas = atual.Skip(melhor).ToList();
            anterior = chaves;
            return novas;
        }
        public void Reiniciar() { anterior = null; }
    }

    // ---------------- dados do jogo (nomes, ícones, preços) ----------------
    class Catalogo
    {
        public Dictionary<string, int> IdPorNome = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, string> NomePorId = new Dictionary<int, string>();
        public Dictionary<string, List<int>> TodosIds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        public List<string> NomesSupply = new List<string>();
        public Dictionary<string, long[]> Precos = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase); // [loot, custo]
        public Dictionary<string, string> ImagemCriatura = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> LookPorMonstro = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly string pasta;
        readonly Dictionary<string, ImageSource> cache = new Dictionary<string, ImageSource>();

        public Catalogo(string pastaApp)
        {
            pasta = pastaApp;
            foreach (var l in LerLinhas("itens.tsv"))
            {
                var p = l.Split('\t'); int id; if (p.Length < 5 || !int.TryParse(p[1], out id)) continue;
                if (p[4] == "1" && !IdPorNome.ContainsKey(p[0])) IdPorNome[p[0]] = id;
                NomePorId[id] = p[0];
                List<int> l2; if (!TodosIds.TryGetValue(p[0], out l2)) TodosIds[p[0]] = l2 = new List<int>(); l2.Add(id);
            }
            foreach (var l in LerLinhas("precos.tsv"))
            {
                var p = l.Split('\t'); long a, b; if (p.Length < 3) continue;
                long.TryParse(p[1], out a); long.TryParse(p[2], out b); Precos[p[0]] = new[] { a, b };
            }
            // preços anotados à mão de NPCs (dados/precos-npc-<NPC>.tsv: nome <TAB> quanto o NPC paga); fica o maior valor
            var dd = Path.Combine(pasta, "dados");
            if (Directory.Exists(dd))
                foreach (var f in Directory.GetFiles(dd, "precos-npc-*.tsv"))
                    foreach (var l in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        var p = l.Split((char)9); long v; if (p.Length < 2 || !long.TryParse(p[1].Replace(".", ""), out v)) continue;
                        long[] e; if (!Precos.TryGetValue(p[0], out e)) Precos[p[0]] = e = new long[2];
                        if (v > e[0]) e[0] = v;
                    }
            foreach (var l in LerLinhas("criaturas.tsv"))
            {
                var p = l.Split('\t'); if (p.Length >= 5 && p[4].Length > 0) ImagemCriatura[p[0]] = p[4];
            }
            foreach (var l in LerLinhas("monstros.tsv"))
            {
                var p = l.Split('\t'); int look; if (p.Length >= 2 && int.TryParse(p[1], out look) && !LookPorMonstro.ContainsKey(p[0])) LookPorMonstro[p[0]] = look;
            }
            // lista de supplies: dados/supplies.json (["Poção de Vida Média", ...])
            var fs = Path.Combine(pasta, "dados", "supplies.json");
            if (File.Exists(fs)) foreach (Match m in Regex.Matches(File.ReadAllText(fs, Encoding.UTF8), "\"([^\"]+)\"")) NomesSupply.Add(m.Groups[1].Value);
        }
        IEnumerable<string> LerLinhas(string nome)
        {
            var f = Path.Combine(pasta, "dados", nome);
            return File.Exists(f) ? File.ReadAllLines(f, Encoding.UTF8) : new string[0];
        }
        ImageSource Carregar(string arquivo)
        {
            if (arquivo == null) return null;
            ImageSource img;
            if (cache.TryGetValue(arquivo, out img)) return img;
            img = null;
            try
            {
                if (File.Exists(arquivo))
                {
                    var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.UriSource = new Uri(arquivo); b.EndInit(); b.Freeze(); img = b;
                }
            }
            catch { }
            cache[arquivo] = img; return img;
        }
        public ImageSource IconeItem(string nome)
        {
            int id; return IdPorNome.TryGetValue(nome, out id) ? Carregar(Path.Combine(pasta, "icones", "itens", id + ".png")) : null;
        }
        public ImageSource IconeCriatura(string nome)
        {
            string arq; if (ImagemCriatura.TryGetValue(nome, out arq)) return Carregar(Path.Combine(pasta, "icones", "criaturas", arq));
            foreach (var ext in new[] { ".gif", ".png" }) { var f = Path.Combine(pasta, "icones", "criaturas", nome + ext); if (File.Exists(f)) return Carregar(f); }
            int look; if (LookPorMonstro.TryGetValue(nome, out look)) return Carregar(Path.Combine(pasta, "icones", "monstros", look + ".png"));
            return null;
        }
    }

    // preços definidos pelo usuário e itens escondidos (ficam em %LOCALAPPDATA%\BloodstoneHunt)
    class Preferencias
    {
        public Dictionary<string, long> Precos = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Escondidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool PorCima = true, ModoObs;
        public Dictionary<string, double[]> Posicoes = new Dictionary<string, double[]>();
        public HashSet<string> Abertas = new HashSet<string>();
        readonly string pasta;
        public Preferencias()
        {
            pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BloodstoneHunt");
            try
            {
                Directory.CreateDirectory(pasta);
                var f = Path.Combine(pasta, "precos.tsv");
                if (File.Exists(f)) foreach (var l in File.ReadAllLines(f, Encoding.UTF8)) { var p = l.Split('\t'); long v; if (p.Length == 2 && long.TryParse(p[1], out v)) Precos[p[0]] = v; }
                f = Path.Combine(pasta, "escondidos.txt");
                if (File.Exists(f)) foreach (var l in File.ReadAllLines(f, Encoding.UTF8)) if (l.Trim().Length > 0) Escondidos.Add(l.Trim());
                f = Path.Combine(pasta, "opcoes.txt");
                if (File.Exists(f))
                    foreach (var l in File.ReadAllLines(f))
                    {
                        var p = l.Split(new[] { "=" }, 2, StringSplitOptions.None); if (p.Length < 2) continue;
                        if (p[0] == "porcima") PorCima = p[1].Trim() != "0";
                        else if (p[0] == "obs") ModoObs = p[1].Trim() == "1";
                        else if (p[0] == "abertas") foreach (var x in p[1].Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries)) Abertas.Add(x.Trim());
                        else if (p[0].StartsWith("pos."))
                        {
                            var xy = p[1].Split(";".ToCharArray()); double x0, y0;
                            if (xy.Length == 2 && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x0) && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y0)) Posicoes[p[0].Substring(4)] = new[] { x0, y0 };
                        }
                    }
            }
            catch { }
        }
        public void Salvar()
        {
            try
            {
                File.WriteAllLines(Path.Combine(pasta, "precos.tsv"), Precos.Select(k => k.Key + "\t" + k.Value), Encoding.UTF8);
                File.WriteAllLines(Path.Combine(pasta, "escondidos.txt"), Escondidos, Encoding.UTF8);
                var o = new List<string> { "porcima=" + (PorCima ? 1 : 0), "obs=" + (ModoObs ? 1 : 0), "abertas=" + string.Join(",", Abertas) };
                foreach (var k in Posicoes) o.Add("pos." + k.Key + "=" + k.Value[0].ToString(CultureInfo.InvariantCulture) + ";" + k.Value[1].ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(Path.Combine(pasta, "opcoes.txt"), o);
            }
            catch { }
        }
        public string PastaLog { get { return pasta; } }
    }

    // ---------------- sessão ----------------
    class ItemLoot { public string Nome, Cor; public long Qtd; }
    class Golpe { public DateTime Quando; public long Valor; }

    class Sessao
    {
        public readonly object Trava = new object();
        public DateTime Inicio = DateTime.Now; public TimeSpan Acumulado = TimeSpan.Zero; public bool Pausada;
        public long XpUltimo = -1, XpGanho, XpAtual;
        public List<KeyValuePair<DateTime, long>> XpAmostras = new List<KeyValuePair<DateTime, long>>();
        public long Dano;
        public Dictionary<string, int> Kills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> DanoPorMonstro = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ItemLoot> Loot = new Dictionary<string, ItemLoot>(StringComparer.OrdinalIgnoreCase);
        public long Recebido, Mana, MaiorGolpe, MaxDps;
        public long Cura, ManaGasta; public int VidaUltima = -1, ManaUltima = -1;
        public StatusJogador Atual;
        public Dictionary<string, long> Supplies = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> RecebidoPorFonte = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> RecebidoPorTipo = new Dictionary<string, long>();
        public List<Golpe> Golpes = new List<Golpe>();

        public TimeSpan Duracao { get { return Acumulado + (Pausada ? TimeSpan.Zero : DateTime.Now - Inicio); } }
        public void Pausar(bool p)
        {
            if (p == Pausada) return;
            if (p) Acumulado += DateTime.Now - Inicio; else Inicio = DateTime.Now;
            Pausada = p;
        }
    }

    static class Analise
    {
        static readonly Regex Tags = new Regex("<[^>]+>");
        static readonly Regex Fonte = new Regex(@"<font[^>]*?color:\s*rgb\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)\)[^>]*>(.*?)</font>", RegexOptions.IgnoreCase);
        static readonly Regex CorQualquer = new Regex(@"color:\s*(?:rgb\((\d+)\s*,\s*(\d+)\s*,\s*(\d+)\)|#([0-9a-f]{6}))", RegexOptions.IgnoreCase);
        static readonly Regex Qtd = new Regex(@"^(\d[\d\.]*)\s+(.+)$");
        static readonly Regex DanoPt = new Regex(@"^(.+?) perdeu ([\d\.,]+) pontos? de vida", RegexOptions.IgnoreCase);
        static readonly Regex DanoEn = new Regex(@"^(.+?) has lost ([\d\.,]+) hit ?points", RegexOptions.IgnoreCase);
        // "Você perdeu 120 pontos de vida." / "Você perdeu 120 pontos de vida do ataque de Rat." (e o equivalente em inglês)
        static readonly Regex RecebidoPt = new Regex(@"^Voc[eê] perdeu ([\d\.,]+) pontos? de vida(?: do ataque de (.+?))?\.?$", RegexOptions.IgnoreCase);
        static readonly Regex RecebidoEn = new Regex(@"^You have lost ([\d\.,]+) (?:attack )?hit ?points(?: of (.+?))?\.?$", RegexOptions.IgnoreCase);
        static readonly Regex ManaRe = new Regex(@"^(?:Voc[eê] perdeu ([\d\.,]+) pontos? de mana|You have lost ([\d\.,]+) mana points?)", RegexOptions.IgnoreCase);

        public static string Limpar(string s) { return System.Net.WebUtility.HtmlDecode(Tags.Replace(s ?? "", "")).Trim(); }
        static long Numero(string s) { long v; long.TryParse((s ?? "").Replace(".", "").Replace(",", ""), out v); return v; }

        public static bool Pilhagem(string texto, out string monstro, out List<ItemLoot> itens)
        {
            monstro = null; itens = new List<ItemLoot>();
            int dp = texto.IndexOf(": "); if (dp <= 0) return false;
            monstro = Limpar(texto.Substring(0, dp));
            string corpo = texto.Substring(dp + 2);
            var partes = new List<KeyValuePair<string, string>>();
            foreach (Match m in Fonte.Matches(corpo))
                partes.Add(new KeyValuePair<string, string>(Limpar(m.Groups[4].Value), string.Format("#{0:X2}{1:X2}{2:X2}", int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value))));
            if (partes.Count == 0)
                foreach (var p in Limpar(corpo).Split(',')) partes.Add(new KeyValuePair<string, string>(p.Trim(), "#969696"));
            foreach (var p in partes)
            {
                string t = p.Key.Trim().TrimEnd('.');
                if (t.Length == 0 || t.Equals("Nenhum", StringComparison.OrdinalIgnoreCase) || t.Equals("Nothing", StringComparison.OrdinalIgnoreCase) || t.Equals("nada", StringComparison.OrdinalIgnoreCase)) continue;
                long q = 1; var mq = Qtd.Match(t);
                if (mq.Success) { q = Numero(mq.Groups[1].Value); t = mq.Groups[2].Value.Trim(); }
                itens.Add(new ItemLoot { Nome = t, Qtd = Math.Max(1, q), Cor = p.Value });
            }
            return monstro.Length > 0 && monstro.Length < 60;
        }
        // dano que VOCÊ causou: "<monstro> perdeu N pontos de vida"
        public static bool Dano(string texto, out string alvo, out long valor)
        {
            alvo = null; valor = 0; var t = Limpar(texto);
            if (t.StartsWith("Você ", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Voce ", StringComparison.OrdinalIgnoreCase) || t.StartsWith("You ", StringComparison.OrdinalIgnoreCase)) return false;
            var m = DanoPt.Match(t); if (!m.Success) m = DanoEn.Match(t);
            if (!m.Success) return false;
            alvo = m.Groups[1].Value.Trim(); valor = Numero(m.Groups[2].Value);
            return valor > 0;
        }
        // dano que você RECEBEU; a cor da mensagem (se houver) indica o tipo de dano
        public static bool Recebido(string texto, out long valor, out string fonte, out string cor)
        {
            valor = 0; fonte = null; cor = null; var t = Limpar(texto);
            var m = RecebidoPt.Match(t); if (!m.Success) m = RecebidoEn.Match(t);
            if (!m.Success) return false;
            valor = Numero(m.Groups[1].Value);
            fonte = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : "Sem origem (campo, veneno…)";
            var c = CorQualquer.Match(texto);
            if (c.Success) cor = c.Groups[4].Success ? "#" + c.Groups[4].Value.ToUpper() : string.Format("#{0:X2}{1:X2}{2:X2}", int.Parse(c.Groups[1].Value), int.Parse(c.Groups[2].Value), int.Parse(c.Groups[3].Value));
            return valor > 0;
        }
        public static bool ManaPerdida(string texto, out long valor)
        {
            valor = 0; var m = ManaRe.Match(Limpar(texto)); if (!m.Success) return false;
            valor = Numero(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value); return valor > 0;
        }
        // nome provisório do elemento a partir da cor (a confirmar com mensagens reais do jogo)
        public static string NomeTipo(string cor)
        {
            if (cor == null) return "Sem tipo";
            var c = (Color)ColorConverter.ConvertFromString(cor);
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max - min < 0.12) return max > 0.8 ? "Físico (branco)" : "Físico (cinza)";
            double h; if (max == r) h = 60 * (((g - b) / (max - min)) % 6); else if (max == g) h = 60 * ((b - r) / (max - min) + 2); else h = 60 * ((r - g) / (max - min) + 4);
            if (h < 0) h += 360;
            if (h < 20 || h >= 340) return "Fogo?";
            if (h < 50) return "Fogo / laranja?";
            if (h < 70) return "Luz?";
            if (h < 160) return "Terra?";
            if (h < 200) return "Ar?";
            if (h < 250) return "Água?";
            return "Trevas?";
        }
    }

    // ---------------- modelo da interface ----------------
    class Base : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        readonly Dictionary<string, object> v = new Dictionary<string, object>();
        protected object G(string k) { object o; return v.TryGetValue(k, out o) ? o : null; }
        public void S(string k, object o) { object a; if (v.TryGetValue(k, out a) && Equals(a, o)) return; v[k] = o; var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(k)); }
    }
    class Linha : Base
    {
        public string Chave { get; set; }
        public string Nome { get; set; }
        public ImageSource Imagem { get; set; }
        public Brush Cor { get { return (Brush)G("Cor"); } }
        public string Valor { get { return (string)G("Valor"); } }
        public string Extra { get { return (string)G("Extra"); } }
        public double Percentual { get { var o = G("Percentual"); return o == null ? 0 : (double)o; } }
    }
    class LootVM : Base
    {
        public string Nome { get; set; }
        public ImageSource Imagem { get; set; }
        public Brush Cor { get { return (Brush)G("Cor"); } }
        public string Quantidade { get { return (string)G("Quantidade"); } }
        public string Dica { get { return (string)G("Dica"); } }
        public double Opacidade { get { var o = G("Opacidade"); return o == null ? 1 : (double)o; } }
    }
    class Painel : Base
    {
        public string Status { get { return (string)G("Status"); } }
        public string StatusCurto { get { return (string)G("StatusCurto"); } }
        public Brush CorStatus { get { return (Brush)G("CorStatus"); } }
        public string IconePausa { get { return (string)G("IconePausa"); } }
        public string Tempo { get { return (string)G("Tempo"); } }
        public string XpTotal { get { return (string)G("XpTotal"); } }
        public string XpHora { get { return (string)G("XpHora"); } }
        public string XpRitmo { get { return (string)G("XpRitmo"); } }
        public double XpRitmoPct { get { var o = G("XpRitmoPct"); return o == null ? 0 : (double)o; } }
        public string XpPorKill { get { return (string)G("XpPorKill"); } }
        public string XpAtual { get { return (string)G("XpAtual"); } }
        public string LootTotal { get { return (string)G("LootTotal"); } }
        public string LootHora { get { return (string)G("LootHora"); } }
        public string LootQtd { get { return (string)G("LootQtd"); } }
        public string Supplies { get { return (string)G("Supplies"); } }
        public string Balanco { get { return (string)G("Balanco"); } }
        public string BalancoHora { get { return (string)G("BalancoHora"); } }
        public Brush CorBalanco { get { return (Brush)G("CorBalanco"); } }
        public string DanoTotal { get { return (string)G("DanoTotal"); } }
        public string DanoHora { get { return (string)G("DanoHora"); } }
        public string RecebidoTotal { get { return (string)G("RecebidoTotal"); } }
        public string RecebidoHora { get { return (string)G("RecebidoHora"); } }
        public string MaxDps { get { return (string)G("MaxDps"); } }
        public string MaiorGolpe { get { return (string)G("MaiorGolpe"); } }
        public string ManaPerdida { get { return (string)G("ManaPerdida"); } }
        public string SuppliesHora { get { return (string)G("SuppliesHora"); } }
        public string SuppliesQtd { get { return (string)G("SuppliesQtd"); } }
        public string SupplyStatus { get { return (string)G("SupplyStatus"); } }
        public string Cura { get { return (string)G("Cura"); } }
        public string CuraHora { get { return (string)G("CuraHora"); } }
        public string ManaGasta { get { return (string)G("ManaGasta"); } }
        public string ManaGastaHora { get { return (string)G("ManaGastaHora"); } }
        public string Vida { get { return (string)G("Vida"); } }
        public string ManaAtual { get { return (string)G("ManaAtual"); } }
        public string Nivel { get { return (string)G("Nivel"); } }
        public double VidaPct { get { var o = G("VidaPct"); return o == null ? 0 : (double)o; } }
        public double ManaPct { get { var o = G("ManaPct"); return o == null ? 0 : (double)o; } }
        public Visibility SemSupplies { get { var o = G("SemSupplies"); return o == null ? Visibility.Visible : (Visibility)o; } }
        public ObservableCollection<Linha> SuppliesLista { get; private set; }
        public string TotalKills { get { return (string)G("TotalKills"); } }
        public string KillsHora { get { return (string)G("KillsHora"); } }
        public string Especies { get { return (string)G("Especies"); } }
        public string KillsMais { get { return (string)G("KillsMais"); } }
        public Visibility SemKills { get { var o = G("SemKills"); return o == null ? Visibility.Visible : (Visibility)o; } }
        public Visibility SemLoot { get { var o = G("SemLoot"); return o == null ? Visibility.Visible : (Visibility)o; } }
        public Visibility SemRecebido { get { var o = G("SemRecebido"); return o == null ? Visibility.Visible : (Visibility)o; } }
        public Visibility TemTipos { get { var o = G("TemTipos"); return o == null ? Visibility.Collapsed : (Visibility)o; } }
        public ObservableCollection<Linha> KillsResumo { get; private set; }
        public ObservableCollection<Linha> Kills { get; private set; }
        public ObservableCollection<LootVM> Loot { get; private set; }
        public ObservableCollection<Linha> RecebidoTipos { get; private set; }
        public ObservableCollection<Linha> RecebidoFontes { get; private set; }
        public Painel()
        {
            KillsResumo = new ObservableCollection<Linha>(); Kills = new ObservableCollection<Linha>(); Loot = new ObservableCollection<LootVM>();
            RecebidoTipos = new ObservableCollection<Linha>(); RecebidoFontes = new ObservableCollection<Linha>(); SuppliesLista = new ObservableCollection<Linha>();
        }
    }

    // ---------------- janelas ----------------
    class Janela
    {
        public string Chave; public Window W; public FrameworkElement Corpo;
    }

    class Programa
    {
        static readonly Brush[] Cores = { Cor("#F2B544"), Cor("#FF5A64"), Cor("#5B9BFF"), Cor("#35D07F"), Cor("#B57CFF"), Cor("#3FC7D9"), Cor("#FF9F43") };
        static Brush Cor(string h) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(h)); b.Freeze(); return b; }

        readonly string pastaApp = AppDomain.CurrentDomain.BaseDirectory;
        Offsets O; Catalogo C; Preferencias P; Sessao S = new Sessao(); Painel V = new Painel();
        LeitorJogo L; Trava trava;
        readonly Dictionary<string, Janela> janelas = new Dictionary<string, Janela>();
        readonly Acompanhante acPilhagem = new Acompanhante(), acServidor = new Acompanhante();
        volatile bool rodando = true; volatile bool reiniciarCanais;
        string statusLeitor = "Procurando o client.exe…", statusCurto = "procurando o jogo"; int nivelStatus;
        StreamWriter log, bruto;

        // chave, título, cor do selo, ícone (Segoe MDL2), largura
        static readonly string[][] Definicoes = {
            new[] { "hunt", "Hunt Analyzer", "#E5484D", "", "196" },
            new[] { "loot", "Loot Analyzer", "#D99A2B", "", "236" },
            new[] { "xp", "XP Analyzer", "#3D7BEA", "", "196" },
            new[] { "kills", "Kills Analyzer", "#8A5CE6", "", "216" },
            new[] { "supplies", "Supplies", "#2E9E6A", "", "216" },
            new[] { "recebido", "Dano Recebido", "#C7334A", "", "216" },
        };

        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { MessageBox.Show(e.Exception.ToString(), Info.Nome); e.Handled = true; };
            using (var r = typeof(Programa).Assembly.GetManifestResourceStream("Estilos.xaml")) app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(r));
            var p = new Programa();
            if (args.Length == 2 && args[0] == "--captura") { p.Captura(args[1]); return; }
            p.Iniciar(app);
        }

        void Log(string s) { try { if (log != null) log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); } catch { } }
        void Bruto(string canal, Mensagem m) { try { if (bruto != null) bruto.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + canal + "] " + m.Hora + " | " + m.Texto); } catch { } }

        void Carregar()
        {
            O = Offsets.Carregar(Path.Combine(pastaApp, "offsets.ini"));
            C = new Catalogo(pastaApp);
            P = new Preferencias();
            L = new LeitorJogo(O);
        }

        void Iniciar(Application app)
        {
            Carregar();
            try { log = new StreamWriter(Path.Combine(P.PastaLog, "analyzer.log"), false, Encoding.UTF8) { AutoFlush = true }; } catch { }
            try { bruto = new StreamWriter(Path.Combine(P.PastaLog, "mensagens.log"), false, Encoding.UTF8) { AutoFlush = true }; } catch { }
            Log("iniciado; itens com ícone=" + C.IdPorNome.Count + " preços=" + C.Precos.Count);

            foreach (var d in Definicoes) Criar(d);
            var hunt = janelas["hunt"];
            Ligar(hunt);
            hunt.W.Closed += (s, e) => { rodando = false; SalvarPosicoes(); P.Salvar(); app.Shutdown(); };
            hunt.W.Show();
            trava = new Trava(hunt.W, (Button)hunt.Corpo.FindName("BtnTravar"), () => janelas.Values.Select(x => x.W));
            trava.Mudou += () => { var b = (Button)hunt.Corpo.FindName("BtnTravar"); b.Content = trava.Ligada ? "" : ""; b.Opacity = trava.Ligada ? 1 : 0.45; };
            Atualizacao.Verificar(hunt.W, Log);
            foreach (var d in Definicoes) if (d[0] != "hunt" && P.Abertas.Contains(d[0])) Mostrar(d[0]);

            new Thread(LacoLeitura) { IsBackground = true, Name = "leitor" }.Start();
            new Thread(LacoSupplies) { IsBackground = true, Name = "supplies", Priority = ThreadPriority.BelowNormal }.Start();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            t.Tick += (s, e) => Atualizar(); t.Start();
            Atualizar();
            app.Run();
        }

        Janela Criar(string[] d)
        {
            FrameworkElement corpo;
            using (var r = typeof(Programa).Assembly.GetManifestResourceStream(Recurso(d[0]))) corpo = (FrameworkElement)XamlReader.Load(r);
            var w = new Window { Title = d[1], Width = double.Parse(d[4], CultureInfo.InvariantCulture), Content = corpo, DataContext = V, Tag = Cor(d[2]), Uid = d[3] };
            w.Style = (Style)Application.Current.Resources["JanelaAnalyzer"];
            // modo OBS: sem transparência (janela "layered" aparece preta na captura BitBlt do OBS)
            
            w.Topmost = P.PorCima;
            try { w.Icon = BitmapFrame.Create(new Uri(Path.Combine(pastaApp, "icone.ico"))); } catch { }
            if (d[0] == "hunt") w.ShowInTaskbar = true;
            double[] pos;
            if (P.Posicoes.TryGetValue(d[0], out pos)) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = pos[0]; w.Top = pos[1]; }
            else { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = SystemParameters.WorkArea.Right - 250 - janelas.Count * 30; w.Top = 80 + janelas.Count * 30; }
            var j = new Janela { Chave = d[0], W = w, Corpo = corpo };
            w.Loaded += (s, e) =>
            {
                w.ApplyTemplate();
                var titulo = (FrameworkElement)w.Template.FindName("PART_Titulo", w);
                titulo.MouseLeftButtonDown += (s2, e2) => { if (e2.ButtonState == MouseButtonState.Pressed) { w.DragMove(); SalvarPosicoes(); } };
                var corpoP = (FrameworkElement)w.Template.FindName("PART_Corpo", w);
                ((Button)w.Template.FindName("PART_Recolher", w)).Click += (s2, e2) => corpoP.Visibility = corpoP.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                ((Button)w.Template.FindName("PART_Fechar", w)).Click += (s2, e2) =>
                {
                    if (j.Chave == "hunt") w.Close();
                    else { w.Hide(); P.Abertas.Remove(j.Chave); P.Salvar(); }
                };
            };
            if (d[0] == "loot") LigarLoot(j);
            if (d[0] == "supplies")
            {
                var ls = (ItemsControl)corpo.FindName("ListaSupplies");
                ls.PreviewMouseLeftButtonUp += (s, e) => { var n = NomeSob(e.OriginalSource); if (n != null) EditarPreco(n, w, true); };
            }
            janelas[d[0]] = j;
            return j;
        }
        static string Recurso(string chave)
        {
            switch (chave) { case "hunt": return "Hunt.xaml"; case "loot": return "Loot.xaml"; case "xp": return "Xp.xaml"; case "kills": return "Kills.xaml"; case "supplies": return "Supplies.xaml"; default: return "Recebido.xaml"; }
        }
        void Mostrar(string chave)
        {
            var j = janelas[chave];
            if (j.W.IsVisible) { j.W.Activate(); return; }
            j.W.Show(); if (trava != null) trava.Aplicar(j.W); P.Abertas.Add(chave); P.Salvar();
        }
        void SalvarPosicoes()
        {
            foreach (var j in janelas.Values) if (j.W.IsLoaded) P.Posicoes[j.Chave] = new[] { j.W.Left, j.W.Top };
            P.Salvar();
        }

        void Ligar(Janela hunt)
        {
            var c = hunt.Corpo;
            ((Button)c.FindName("BtnPausar")).Click += (s, e) => { lock (S.Trava) S.Pausar(!S.Pausada); };
            ((Button)c.FindName("BtnZerar")).Click += (s, e) =>
            {
                if (MessageBox.Show(hunt.W, "Zerar a sessão? Todo o progresso desta caçada será perdido.", Info.Nome, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                lock (S.Trava) { var xp = S.XpUltimo; var vi = S.VidaUltima; var ma = S.ManaUltima; var at = S.Atual; S = new Sessao(); S.XpUltimo = xp; S.VidaUltima = vi; S.ManaUltima = ma; S.Atual = at; }
                reiniciarCanais = true;
                V.Loot.Clear(); V.Kills.Clear(); V.KillsResumo.Clear(); V.SuppliesLista.Clear(); V.RecebidoFontes.Clear(); V.RecebidoTipos.Clear();
            };
            ((Button)c.FindName("BtnTravar")).Click += (s, e) => { if (trava != null) trava.Alternar(); };
            ((Button)c.FindName("BtnCopiar")).Click += (s, e) =>
            {
                try { Clipboard.SetText(TextoHunt()); var bc = (Button)s; bc.Content = ""; bc.ToolTip = "Resumo copiado!"; var tm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) }; tm.Tick += (s2, e2) => { bc.Content = ""; bc.ToolTip = "Copiar o resumo da hunt"; tm.Stop(); }; tm.Start(); }
                catch (Exception ex) { MessageBox.Show(hunt.W, "Não foi possível copiar: " + ex.Message, Info.Nome); }
            };
            var fixar = (Button)c.FindName("BtnFixar");
            fixar.Opacity = P.PorCima ? 1 : 0.45;
            fixar.Click += (s, e) =>
            {
                P.PorCima = !P.PorCima; foreach (var j in janelas.Values) j.W.Topmost = P.PorCima;
                fixar.Opacity = P.PorCima ? 1 : 0.45; P.Salvar();
            };
            ((Button)c.FindName("AbrirLoot")).Click += (s, e) => Mostrar("loot");
            ((Button)c.FindName("AbrirXp")).Click += (s, e) => Mostrar("xp");
            ((Button)c.FindName("AbrirKills")).Click += (s, e) => Mostrar("kills");
            ((Button)c.FindName("AbrirRecebido")).Click += (s, e) => Mostrar("recebido");
            ((Button)c.FindName("AbrirSupplies")).Click += (s, e) => Mostrar("supplies");
        }
        void LigarLoot(Janela j)
        {
            var lista = (ItemsControl)j.Corpo.FindName("ListaLoot");
            lista.PreviewMouseLeftButtonUp += (s, e) => { var n = NomeSob(e.OriginalSource); if (n != null) EditarPreco(n, j.W); };
            lista.PreviewMouseRightButtonUp += (s, e) =>
            {
                var n = NomeSob(e.OriginalSource); if (n == null) return;
                if (!P.Escondidos.Remove(n)) P.Escondidos.Add(n);
                P.Salvar(); e.Handled = true;
            };
        }
        static string NomeSob(object o)
        {
            var d = o as DependencyObject;
            while (d != null) { var b = d as Border; if (b != null && b.Tag is string) return (string)b.Tag; d = VisualTreeHelper.GetParent(d); }
            return null;
        }

        // aviso rápido no status do Hunt Analyzer
        void Aviso(Janela hunt, string texto) { var antes = statusCurto; statusCurto = texto; nivelAviso = DateTime.Now; }
        DateTime nivelAviso = DateTime.MinValue;

        // resumo da hunt em texto, para colar em qualquer lugar
        string TextoHunt()
        {
            TimeSpan dur; long xp, dano, rec, cura; List<KeyValuePair<string, int>> kills; List<ItemLoot> loot; List<KeyValuePair<string, long>> sup;
            lock (S.Trava)
            {
                dur = S.Duracao; xp = S.XpGanho; dano = S.Dano; rec = S.Recebido; cura = S.Cura;
                kills = S.Kills.OrderByDescending(k => k.Value).ToList();
                loot = S.Loot.Values.Where(i => !P.Escondidos.Contains(i.Nome)).Select(i => new ItemLoot { Nome = i.Nome, Qtd = i.Qtd }).ToList();
                sup = S.Supplies.OrderByDescending(k => k.Value).ToList();
            }
            double h = Math.Max(dur.TotalHours, 1.0 / 60);
            long vLoot = loot.Sum(i => Preco(i.Nome) * i.Qtd), vSup = sup.Sum(s => s.Value * CustoSupply(s.Key)), bal = vLoot - vSup;
            var sb = new StringBuilder();
            sb.AppendLine("=== Hunt · " + Info.Nome + " ===");
            sb.AppendLine("Sessão: " + string.Format("{0:00}:{1:00}h", (int)dur.TotalHours, dur.Minutes) + " (" + DateTime.Now.ToString("dd/MM/yyyy HH:mm") + ")");
            sb.AppendLine("XP ganho: " + Inteiro(xp) + " (" + Inteiro((long)(xp / h)) + "/h)");
            sb.AppendLine("Loot: " + Inteiro(vLoot) + " gp");
            sb.AppendLine("Supplies: " + Inteiro(vSup) + " gp");
            sb.AppendLine("Balanço: " + Inteiro(bal) + " gp (" + Inteiro((long)(bal / h)) + "/h)");
            sb.AppendLine("Dano: " + Inteiro(dano) + " (" + Inteiro((long)(dano / h)) + "/h)");
            sb.AppendLine("Dano recebido: " + Inteiro(rec) + " · Cura: " + Inteiro(cura));
            if (kills.Count > 0)
            {
                sb.AppendLine("Monstros mortos (" + Inteiro(kills.Sum(k => k.Value)) + "):");
                foreach (var k in kills) sb.AppendLine("  " + Inteiro(k.Value) + "x " + k.Key);
            }
            if (loot.Count > 0)
            {
                sb.AppendLine("Loot:");
                foreach (var i in loot.OrderByDescending(i => Preco(i.Nome) * i.Qtd).ThenByDescending(i => i.Qtd)) { long p = Preco(i.Nome); sb.AppendLine("  " + Inteiro(i.Qtd) + "x " + i.Nome + (p > 0 ? " (" + Inteiro(p * i.Qtd) + " gp)" : "")); }
            }
            if (sup.Count > 0)
            {
                sb.AppendLine("Supplies usados:");
                foreach (var s in sup) { long c = CustoSupply(s.Key); sb.AppendLine("  " + Inteiro(s.Value) + "x " + s.Key + (c > 0 ? " (" + Inteiro(c * s.Value) + " gp)" : "")); }
            }
            return sb.ToString().TrimEnd();
        }

        long Preco(string item)
        {
            long v; if (P.Precos.TryGetValue(item, out v)) return v;
            long[] p; return C.Precos.TryGetValue(item, out p) ? p[0] : 0;
        }

        void EditarPreco(string item, Window dono) { EditarPreco(item, dono, false); }
        // custo=true edita o custo de supply (guardado como "custo:" + nome, separado do preço de loot)
        void EditarPreco(string item, Window dono, bool custo)
        {
            string chave = custo ? "custo:" + item : item;
            var w = new Window { Title = "Preço", Width = 230, Owner = dono, WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true, Tag = Cor("#D99A2B"), Uid = "" };
            w.Style = (Style)Application.Current.Resources["JanelaAnalyzer"];
            var caixa = new TextBox { Text = (custo ? CustoSupply(item) : Preco(item)).ToString(), FontSize = 13, Padding = new Thickness(6, 3, 6, 3), Background = Cor("#101216"), Foreground = Brushes.White, BorderBrush = Cor("#3A3F49"), CaretBrush = Brushes.White, Margin = new Thickness(0, 8, 0, 8) };
            var ok = new Button { Content = "Salvar", IsDefault = true, Style = (Style)Application.Current.Resources["BotaoAtalho"] };
            var canc = new Button { Content = "Cancelar", IsCancel = true, Style = (Style)Application.Current.Resources["BotaoAtalho"] };
            var linha = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            linha.Children.Add(canc); linha.Children.Add(ok);
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = item, Foreground = Brushes.White, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            long[] w0; bool tem = C.Precos.TryGetValue(item, out w0);
            string ref0 = custo ? (tem && w0[1] > 0 ? "NPC vende (wiki): " + w0[1].ToString("N0") + " gp" : "Sem preço de venda na wiki")
                                : (tem && w0[0] > 0 ? "NPC paga (wiki): " + w0[0].ToString("N0") + " gp" : "Sem preço de NPC na wiki");
            st.Children.Add(new TextBlock { Text = ref0, Foreground = Cor("#8B909A"), FontSize = 10.5, Margin = new Thickness(0, 2, 0, 0) });
            st.Children.Add(caixa); st.Children.Add(linha);
            w.Content = st;
            canc.Click += (s, e) => w.Close();
            ok.Click += (s, e) =>
            {
                long v; var t = caixa.Text.Replace(".", "").Replace(",", "").Replace("gp", "").Trim();
                if (t.Length == 0) { P.Precos.Remove(chave); w.DialogResult = true; return; }
                if (!long.TryParse(t, out v) || v < 0) { caixa.BorderBrush = Brushes.IndianRed; return; }
                P.Precos[chave] = v; w.DialogResult = true;
            };
            w.Loaded += (s, e) =>
            {
                w.ApplyTemplate();
                ((Button)w.Template.FindName("PART_Fechar", w)).Click += (s2, e2) => w.Close();
                ((FrameworkElement)w.Template.FindName("PART_Recolher", w)).Visibility = Visibility.Collapsed;
                ((FrameworkElement)w.Template.FindName("PART_Titulo", w)).MouseLeftButtonDown += (s2, e2) => w.DragMove();
                caixa.Focus(); caixa.SelectAll();
            };
            if (w.ShowDialog() == true) P.Salvar();
        }

        // ---------------- laço de leitura (thread separada) ----------------
        void LacoLeitura()
        {
            DateTime ultimaBusca = DateTime.MinValue;
            Dictionary<string, uint> canais = null; DateTime canaisEm = DateTime.MinValue;
            while (rodando)
            {
                try
                {
                    if (L.M == null || !L.M.Vivo)
                    {
                        if (L.M != null) { L.M.Dispose(); L.M = null; Log("jogo fechado"); }
                        if (DateTime.Now - ultimaBusca < TimeSpan.FromSeconds(2)) { Thread.Sleep(250); continue; }
                        ultimaBusca = DateTime.Now;
                        var todos = Process.GetProcessesByName("client");
                        var p = todos.FirstOrDefault(x => { try { return x.MainWindowTitle.IndexOf("BloodStone", StringComparison.OrdinalIgnoreCase) >= 0; } catch { return false; } }) ?? todos.FirstOrDefault();
                        if (p == null) { Status("Aguardando o client.exe…", "aguardando o jogo", 0); continue; }
                        string erro; L.M = Memoria.Abrir(p, out erro);
                        if (L.M == null) { Status(erro, "sem acesso ao jogo", 2); continue; }
                        Log(string.Format("conectado PID {0} base=0x{1:X}", p.Id, L.M.Base));
                        canais = null; acPilhagem.Reiniciar(); acServidor.Reiniciar();
                        lock (S.Trava) { S.XpUltimo = -1; }
                    }
                    if (reiniciarCanais) { reiniciarCanais = false; acPilhagem.Reiniciar(); acServidor.Reiniciar(); }

                    long xp;
                    bool temXp = L.LerXp(out xp);
                    if (temXp)
                        lock (S.Trava)
                        {
                            if (S.XpUltimo < 0) S.XpUltimo = xp;
                            long d = xp - S.XpUltimo;
                            if (d > 0 && d < 2000000000 && !S.Pausada) S.XpGanho += d;   // queda (morte) não desconta
                            S.XpUltimo = xp; S.XpAtual = xp;
                        }

                    // vida e mana: subida de vida = cura (inclui regeneração); queda de mana = mana gasta
                    var st = L.LerStatus();
                    if (st == null) { if (!semStatusAvisado) { Log("vida/mana não lidas"); semStatusAvisado = true; } } else semStatusAvisado = false;
                    if (st != null)
                        lock (S.Trava)
                        {
                            if (!S.Pausada && S.Atual != null && S.Atual.VidaMax == st.VidaMax && S.VidaUltima >= 0 && st.Vida > S.VidaUltima) S.Cura += st.Vida - S.VidaUltima;
                            if (!S.Pausada && S.Atual != null && S.Atual.ManaMax == st.ManaMax && S.ManaUltima >= 0 && st.Mana < S.ManaUltima) S.ManaGasta += S.ManaUltima - st.Mana;
                            S.VidaUltima = st.Vida; S.ManaUltima = st.Mana; S.Atual = st;
                        }

                    if (canais == null || DateTime.Now - canaisEm > TimeSpan.FromSeconds(5) || !canais.ContainsKey("Pilhagem"))
                    { var antes = canais == null ? "" : string.Join(",", canais.Keys); canais = L.Canais(); canaisEm = DateTime.Now; var agora = string.Join(",", canais.Keys); if (agora != antes) Log("canais: " + (agora.Length > 0 ? agora : "(nenhum)")); }

                    uint cp, cs;
                    bool temP = canais.TryGetValue("Pilhagem", out cp) || canais.TryGetValue("Loot", out cp);
                    bool temS = canais.TryGetValue("Servidor", out cs) || canais.TryGetValue("Server", out cs);
                    if (temP) foreach (var m in acPilhagem.Novas(L.Mensagens(cp, 200))) { Bruto("Pilhagem", m); Pilhagem(m); }
                    if (temS) foreach (var m in acServidor.Novas(L.Mensagens(cs, 200))) { Bruto("Servidor", m); Servidor(m); }

                    if (!temXp) Status("Conectado, mas o XP não foi lido. Os endereços podem ter mudado (offsets.ini).", "XP não lido", 2);
                    else if (!temP) Status("Conectado. Abra o canal de Pilhagem no jogo para contar loot e kills.", "abra o canal Pilhagem", 0);
                    else Status("Conectado ao jogo · PID " + L.M.Pid, "conectado", 1);
                }
                catch (Exception ex) { Log("erro: " + ex.Message); Status("Erro de leitura: " + ex.Message, "erro de leitura", 2); }
                Thread.Sleep(300);
            }
        }
        void Status(string s, string curto, int nivel) { if (s != statusLeitor) Log("status: " + s); statusLeitor = s; statusCurto = curto; nivelStatus = nivel; }

        // ---------------- supplies (thread própria) ----------------
        // O jogo guarda no tipo de cada item o total que o personagem carrega (o mesmo número da barra de atalhos),
        // em tipo + ItemTotal, atualizado pelo servidor mesmo com a bolsa fechada. A cada segundo lê o total dos supplies:
        // queda = consumo. Subida (compra, loot) é ignorada; queda de mais de 20 de uma vez (guardar no depot, jogar fora,
        // relogar) também é ignorada; só conta com o personagem logado (vida/mana lidas).
        string supplyStatus = ""; bool semStatusAvisado;
        void LacoSupplies()
        {
            Memoria memo = null; Dictionary<uint, int> tipos = null; var totais = new Dictionary<uint, long>();
            while (rodando)
            {
                try
                {
                    var m = L.M;
                    if (m == null) { Thread.Sleep(1000); continue; }
                    if (m != memo) { memo = m; tipos = null; totais.Clear(); }
                    if (tipos == null || tipos.Count == 0)
                    {
                        var ids = C.NomesSupply.SelectMany(n => { List<int> l; return C.TodosIds.TryGetValue(n, out l) ? l : new List<int>(); });
                        tipos = L.TiposSupply(ids);
                        Log("supplies: tipos=" + tipos.Count);
                    }
                    bool logado; lock (S.Trava) logado = S.Atual != null && !semStatusAvisado;
                    var usados = new Dictionary<int, long>(); int carregando = 0;
                    foreach (var kv in tipos)
                    {
                        uint v; if (!m.U32(kv.Key + O.ItemTotal, out v) || v > 1000000) continue;
                        if (v > 0) carregando++;
                        long antes;
                        if (logado && totais.TryGetValue(kv.Key, out antes) && v < antes && antes - v <= 20) Somar(usados, kv.Value, antes - v);
                        totais[kv.Key] = v;
                    }
                    supplyStatus = carregando + " tipos de supply no personagem";
                    if (usados.Count > 0)
                        lock (S.Trava)
                        {
                            if (!S.Pausada)
                                foreach (var u in usados)
                                {
                                    string nome; if (!C.NomePorId.TryGetValue(u.Key, out nome)) nome = "Item " + u.Key;
                                    long a; S.Supplies.TryGetValue(nome, out a); S.Supplies[nome] = a + u.Value;
                                    Log("supply usado: " + u.Value + "x " + nome);
                                }
                        }
                }
                catch (Exception ex) { Log("supplies erro: " + ex.Message); }
                Thread.Sleep(1000);
            }
        }
        static void Somar(Dictionary<int, long> d, int k, long v) { long a; d.TryGetValue(k, out a); d[k] = a + v; }
        long CustoSupply(string nome)
        {
            long v; if (P.Precos.TryGetValue("custo:" + nome, out v)) return v;
            long[] p; return C.Precos.TryGetValue(nome, out p) ? p[1] : 0;
        }

        void Pilhagem(Mensagem m)
        {
            string monstro; List<ItemLoot> itens;
            if (!Analise.Pilhagem(m.Texto, out monstro, out itens)) return;
            Log("pilhagem: " + Analise.Limpar(m.Texto));
            lock (S.Trava)
            {
                if (S.Pausada) return;
                int k; S.Kills.TryGetValue(monstro, out k); S.Kills[monstro] = k + 1;
                foreach (var it in itens)
                {
                    ItemLoot e; if (!S.Loot.TryGetValue(it.Nome, out e)) S.Loot[it.Nome] = e = new ItemLoot { Nome = it.Nome, Cor = it.Cor };
                    e.Qtd += it.Qtd;
                }
            }
        }
        void Servidor(Mensagem m)
        {
            string alvo, fonte, cor; long v;
            if (Analise.Recebido(m.Texto, out v, out fonte, out cor))
            {
                lock (S.Trava)
                {
                    if (S.Pausada) return;
                    S.Recebido += v; S.MaiorGolpe = Math.Max(S.MaiorGolpe, v);
                    long d; S.RecebidoPorFonte.TryGetValue(fonte, out d); S.RecebidoPorFonte[fonte] = d + v;
                    string tipo = cor ?? "";
                    S.RecebidoPorTipo.TryGetValue(tipo, out d); S.RecebidoPorTipo[tipo] = d + v;
                    var agora = DateTime.Now;
                    S.Golpes.Add(new Golpe { Quando = agora, Valor = v });
                    S.Golpes.RemoveAll(g => (agora - g.Quando).TotalSeconds > 1.0);
                    S.MaxDps = Math.Max(S.MaxDps, S.Golpes.Sum(g => g.Valor));
                }
                return;
            }
            if (Analise.ManaPerdida(m.Texto, out v)) { lock (S.Trava) if (!S.Pausada) S.Mana += v; return; }
            if (Analise.Dano(m.Texto, out alvo, out v))
                lock (S.Trava)
                {
                    if (S.Pausada) return;
                    S.Dano += v;
                    long d; S.DanoPorMonstro.TryGetValue(alvo, out d); S.DanoPorMonstro[alvo] = d + v;
                }
        }

        // ---------------- atualização da tela ----------------
        static readonly CultureInfo BR = CultureInfo.GetCultureInfo("pt-BR");
        static string Num(double v)
        {
            double a = Math.Abs(v);
            if (a >= 1e9) return (v / 1e9).ToString(a >= 1e10 ? "0.#" : "0.##", BR) + " kkk";
            if (a >= 1e6) return (v / 1e6).ToString(a >= 1e7 ? "0.#" : "0.##", BR) + " kk";
            if (a >= 1e4) return (v / 1e3).ToString("0.#", BR) + " k";
            return v.ToString("N0", BR);
        }
        static string Inteiro(long v) { return v.ToString("N0", BR); }

        void Atualizar()
        {
            V.S("Status", statusLeitor); V.S("StatusCurto", statusCurto);
            V.S("CorStatus", nivelStatus == 1 ? Cor("#35D07F") : nivelStatus == 2 ? Cor("#FF5A64") : Cor("#F2B544"));

            TimeSpan dur; long xp, xpAtual, dano, rec, mana, maiorG, maxDps, cura, manaGasta; bool pausada; double ritmo = -1; StatusJogador atual; List<KeyValuePair<string, long>> sup;
            List<KeyValuePair<string, int>> kills; List<ItemLoot> loot; Dictionary<string, long> danoM;
            List<KeyValuePair<string, long>> fontes, tipos;
            lock (S.Trava)
            {
                dur = S.Duracao; xp = S.XpGanho; xpAtual = S.XpAtual; dano = S.Dano; pausada = S.Pausada;
                rec = S.Recebido; mana = S.Mana; maiorG = S.MaiorGolpe; maxDps = S.MaxDps; cura = S.Cura; manaGasta = S.ManaGasta; atual = S.Atual; sup = S.Supplies.OrderByDescending(k => k.Value * CustoSupply(k.Key)).ThenByDescending(k => k.Value).ToList();
                kills = S.Kills.OrderByDescending(k => k.Value).ToList();
                loot = S.Loot.Values.Select(i => new ItemLoot { Nome = i.Nome, Qtd = i.Qtd, Cor = i.Cor }).ToList();
                danoM = new Dictionary<string, long>(S.DanoPorMonstro, StringComparer.OrdinalIgnoreCase);
                fontes = S.RecebidoPorFonte.OrderByDescending(k => k.Value).ToList();
                tipos = S.RecebidoPorTipo.OrderByDescending(k => k.Value).ToList();
                // ritmo de XP: amostra a cada ~20 s, compara com 10 min atrás
                var agora = DateTime.Now;
                if (!pausada && (S.XpAmostras.Count == 0 || (agora - S.XpAmostras[S.XpAmostras.Count - 1].Key).TotalSeconds >= 20)) S.XpAmostras.Add(new KeyValuePair<DateTime, long>(agora, xp));
                S.XpAmostras.RemoveAll(a => (agora - a.Key).TotalMinutes > 10.5);
                if (S.XpAmostras.Count > 1)
                {
                    var a0 = S.XpAmostras[0]; double h = (agora - a0.Key).TotalHours;
                    if (h > 1.0 / 60) ritmo = (xp - a0.Value) / h;
                }
            }
            double horas = Math.Max(dur.TotalHours, 1.0 / 60);   // evita números absurdos no 1º minuto
            V.S("Tempo", string.Format("{0:00}:{1:00}h", (int)dur.TotalHours, dur.Minutes));
            V.S("IconePausa", pausada ? "" : "");

            long valorLoot = 0, qtdItens = 0;
            foreach (var i in loot) { if (P.Escondidos.Contains(i.Nome)) continue; valorLoot += Preco(i.Nome) * i.Qtd; qtdItens += i.Qtd; }
            long supplies = sup.Sum(s => s.Value * CustoSupply(s.Key)), balanco = valorLoot - supplies;

            V.S("XpTotal", Inteiro(xp)); V.S("XpHora", Inteiro((long)(xp / horas))); V.S("XpAtual", xpAtual > 0 ? Inteiro(xpAtual) : "—");
            V.S("XpRitmo", ritmo < 0 ? "—" : Inteiro((long)ritmo));
            V.S("XpRitmoPct", ritmo < 0 || xp == 0 ? 0.0 : Math.Min(100, 50.0 * ritmo / (xp / horas)));
            int totalK = kills.Sum(k => k.Value);
            V.S("XpPorKill", totalK == 0 ? "—" : Inteiro(xp / totalK));
            V.S("LootTotal", Inteiro(valorLoot)); V.S("LootHora", Inteiro((long)(valorLoot / horas))); V.S("LootQtd", Inteiro(qtdItens));
            V.S("Supplies", Inteiro(supplies)); V.S("SuppliesHora", Inteiro((long)(supplies / horas))); V.S("SuppliesQtd", Inteiro(sup.Sum(s => s.Value)));
            V.S("SupplyStatus", supplyStatus.Length > 0 ? supplyStatus : "procurando poções nas bolsas abertas…");
            V.S("SemSupplies", sup.Count == 0 ? Visibility.Visible : Visibility.Collapsed);
            V.S("Cura", Inteiro(cura)); V.S("CuraHora", Inteiro((long)(cura / horas))); V.S("ManaGasta", Inteiro(manaGasta)); V.S("ManaGastaHora", Inteiro((long)(manaGasta / horas)));
            V.S("Vida", atual == null ? "—" : Inteiro(atual.Vida) + " / " + Inteiro(atual.VidaMax)); V.S("ManaAtual", atual == null ? "—" : Inteiro(atual.Mana) + " / " + Inteiro(atual.ManaMax));
            V.S("VidaPct", atual == null ? 0.0 : 100.0 * atual.Vida / atual.VidaMax); V.S("ManaPct", atual == null || atual.ManaMax == 0 ? 0.0 : 100.0 * atual.Mana / atual.ManaMax);
            V.S("Nivel", atual == null ? "—" : atual.Nivel.ToString());
            V.S("Balanco", Inteiro(balanco)); V.S("BalancoHora", Inteiro((long)(balanco / horas)));
            V.S("CorBalanco", balanco >= 0 ? Cor("#35D07F") : Cor("#FF5A64"));
            V.S("DanoTotal", Inteiro(dano)); V.S("DanoHora", Inteiro((long)(dano / horas)));
            V.S("RecebidoTotal", Inteiro(rec)); V.S("RecebidoHora", Inteiro((long)(rec / horas)));
            V.S("MaxDps", Inteiro(maxDps)); V.S("MaiorGolpe", Inteiro(maiorG)); V.S("ManaPerdida", Inteiro(mana));
            V.S("TotalKills", Inteiro(totalK)); V.S("KillsHora", (totalK / horas).ToString("0.#", BR)); V.S("Especies", kills.Count.ToString());
            V.S("SemKills", totalK == 0 ? Visibility.Visible : Visibility.Collapsed);
            V.S("SemLoot", loot.Count == 0 ? Visibility.Visible : Visibility.Collapsed);
            V.S("SemRecebido", rec == 0 ? Visibility.Visible : Visibility.Collapsed);
            V.S("TemTipos", tipos.Any(t => t.Key != "") ? Visibility.Visible : Visibility.Collapsed);
            V.S("KillsMais", kills.Count > 6 ? "+ " + (kills.Count - 6) + " espécies" : "");

            // resumo no Hunt Analyzer: "103x dark torturer"
            Sincronizar(V.KillsResumo, kills.Take(6).Select(k => k.Key).ToList(), n => new Linha { Chave = n, Nome = n }, x => x.Chave);
            for (int i = 0; i < V.KillsResumo.Count; i++) V.KillsResumo[i].S("Valor", Inteiro(kills[i].Value));

            Sincronizar(V.Kills, kills.Select(k => k.Key).ToList(), n => new Linha { Chave = n, Nome = n, Imagem = C.IconeCriatura(n) }, x => x.Chave);
            for (int i = 0; i < V.Kills.Count; i++)
            {
                var k = V.Kills[i]; int q = kills[i].Value; long dm; danoM.TryGetValue(k.Nome, out dm);
                k.S("Cor", Cores[i % Cores.Length]); k.S("Valor", Inteiro(q) + "x");
                k.S("Extra", dm > 0 ? "  " + Num(dm) : ""); k.S("Percentual", totalK == 0 ? 0.0 : 100.0 * q / totalK);
            }

            Linhas(V.RecebidoFontes, fontes, rec, (n, i) => Cores[(i + 1) % Cores.Length], n => n);
            Sincronizar(V.SuppliesLista, sup.Select(s => s.Key).ToList(), n => new Linha { Chave = n, Nome = n, Imagem = C.IconeItem(n) }, x => x.Chave);
            for (int i = 0; i < V.SuppliesLista.Count; i++)
            {
                long c = CustoSupply(sup[i].Key), q = sup[i].Value;
                V.SuppliesLista[i].S("Valor", Inteiro(q) + "x"); V.SuppliesLista[i].S("Extra", c > 0 ? "  " + Num(c * q) + " gp" : "  sem preço");
            }
            Linhas(V.RecebidoTipos, tipos.Where(t => t.Key != "").ToList(), rec, (n, i) => Cor(n), Analise.NomeTipo);

            // loot: mais valioso primeiro; escondidos no fim e apagados
            var ordem = loot.OrderBy(i => P.Escondidos.Contains(i.Nome) ? 1 : 0).ThenByDescending(i => Preco(i.Nome) * i.Qtd).ThenByDescending(i => i.Qtd).ToList();
            Sincronizar(V.Loot, ordem.Select(i => i.Nome).ToList(), n => new LootVM { Nome = n, Imagem = C.IconeItem(n) }, x => x.Nome);
            for (int i = 0; i < V.Loot.Count; i++)
            {
                var vm = V.Loot[i]; var it = ordem[i]; long pr = Preco(it.Nome); bool esc = P.Escondidos.Contains(it.Nome);
                vm.S("Quantidade", it.Qtd >= 10000 ? Num(it.Qtd).Replace(" ", "") : Inteiro(it.Qtd));
                vm.S("Cor", Cor(it.Cor == null || it.Cor == "#969696" ? "#30343C" : it.Cor));
                vm.S("Opacidade", esc ? 0.35 : 1.0);
                vm.S("Dica", it.Nome + "\n" + Inteiro(it.Qtd) + " × " + Inteiro(pr) + " gp = " + Inteiro(pr * it.Qtd) + " gp" + (esc ? "\n(escondido: não conta no total)" : pr == 0 ? "\n(sem preço: clique para definir)" : ""));
            }
        }
        void Linhas(ObservableCollection<Linha> col, List<KeyValuePair<string, long>> dados, long total, Func<string, int, Brush> cor, Func<string, string> nome)
        {
            Sincronizar(col, dados.Select(d => d.Key).ToList(), k => new Linha { Chave = k, Nome = nome(k) }, x => x.Chave);
            for (int i = 0; i < col.Count; i++)
            {
                double pc = total == 0 ? 0 : 100.0 * dados[i].Value / total;
                col[i].S("Cor", cor(dados[i].Key, i)); col[i].S("Valor", Num(dados[i].Value)); col[i].S("Extra", string.Format(BR, "  {0:0.0}%", pc)); col[i].S("Percentual", pc);
            }
        }
        // mantém os itens já criados (evita piscar); a chave nula força recriar quando a ordem muda
        static void Sincronizar<T>(ObservableCollection<T> col, List<string> chaves, Func<string, T> novo, Func<T, string> chave) where T : class
        {
            var mapa = new Dictionary<string, T>();
            foreach (var x in col) { var k = chave(x); if (k != null && !mapa.ContainsKey(k)) mapa[k] = x; }
            for (int i = 0; i < chaves.Count; i++)
            {
                T atual;
                if (!mapa.TryGetValue(chaves[i], out atual)) { atual = novo(chaves[i]); mapa[chaves[i]] = atual; }
                int j = col.IndexOf(atual);
                if (j == i) continue;
                if (j >= 0) col.Move(j, i); else col.Insert(i, atual);
            }
            while (col.Count > chaves.Count) col.RemoveAt(col.Count - 1);
        }

        // ---------------- modo de teste: desenha as janelas com dados de exemplo e salva em PNG ----------------
        void Captura(string arquivo)
        {
            Carregar();
            P.PorCima = false;
            foreach (var d in Definicoes) Criar(d);
            var ex = new[] {
                "Cockroach: <font style='color: rgb(150,150,150)'>2 Pata de Barata</font>, <font style='color: rgb(150,150,150)'>Punhado de Terra</font>",
                "Rat: <font style='color: rgb(150,150,150)'>Rabo de Rato</font>, <font style='color: rgb(150,150,150)'>2 Queijo Mofado</font>",
                "Cockroach: Nenhum", "Chicken: <font style='color: rgb(150,150,150)'>3 Ovo de Galinha</font>", "Cat: Nenhum",
                "Bodovelha: Nenhum", "Rat: <font style='color: rgb(80,160,255)'>Ametista</font>", "Cockroach: <font style='color: rgb(150,150,150)'>6 Punhado de Terra</font>, <font style='color: rgb(150,150,150)'>Ovo de Inseto</font>",
                "Porcoelho: Nenhum", "Cockroach: <font style='color: rgb(150,150,150)'>Casca de Barata</font>" };
            foreach (var e in ex) Pilhagem(new Mensagem { Texto = e });
            foreach (var d in new[] { "Cockroach perdeu 446 pontos de vida.", "Rat perdeu 453 pontos de vida.", "Chicken perdeu 397 pontos de vida.", "Cat perdeu 1210 pontos de vida.",
                                      "Você perdeu 120 pontos de vida do ataque de Rat.", "Você perdeu 80 pontos de vida do ataque de Cockroach.", "Você perdeu 35 pontos de vida.", "Você perdeu 40 pontos de mana." })
                Servidor(new Mensagem { Texto = d });
            S.XpGanho = 184320; S.XpAtual = 112818940; S.Cura = 3120; S.ManaGasta = 2210; S.Atual = new StatusJogador { Nivel = 166, Vida = 2724, VidaMax = 2890, Mana = 3235, ManaMax = 3350 };
            S.Supplies["Poção de Mana Média"] = 7; S.Supplies["Elixir de Vida Médio"] = 1; supplyStatus = "5 tipos de supply no personagem"; S.Acumulado = TimeSpan.FromMinutes(23.5); S.Pausada = true; S.Pausar(false);
            Status("Conectado ao jogo · PID 17632", "conectado", 1);
            Atualizar();
            File.WriteAllText(Path.ChangeExtension(arquivo, ".txt"), TextoHunt(), Encoding.UTF8);
            var imgs = new List<BitmapSource>();
            foreach (var d in Definicoes)
            {
                var w = janelas[d[0]].W; w.Left = -10000; w.ShowInTaskbar = false; w.Show();
                w.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
                w.UpdateLayout();
                var raiz = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
                var rtb = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(raiz); imgs.Add(rtb);
            }
            int larg = imgs.Sum(i => i.PixelWidth) + 10 * (imgs.Count + 1), alt = imgs.Max(i => i.PixelHeight) + 20;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Cor("#0E0F12"), null, new Rect(0, 0, larg, alt));
                double x = 10; foreach (var i in imgs) { dc.DrawImage(i, new Rect(x, 10, i.PixelWidth, i.PixelHeight)); x += i.PixelWidth + 10; }
            }
            var fim = new RenderTargetBitmap(larg, alt, 96, 96, PixelFormats.Pbgra32); fim.Render(dv);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(fim));
            using (var f = File.Create(arquivo)) enc.Save(f);
            foreach (var j in janelas.Values) j.W.Close();
        }
    }
}
