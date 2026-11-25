// Só no teste-captura.exe: procura o nome do personagem na memória do jogo, para descobrir o offset "nome" do offsets.ini.
//   teste-captura.exe --achar-nome resultado.txt        (com o personagem logado; como administrador se o jogo estiver assim)
// Lista, para cada jogador do mapa, os textos (QString) encontrados nos primeiros 0x600 bytes do nó. O nó marcado com *
// é o do personagem (o mesmo de onde vêm vida e mana).
#if TESTE
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace BloodstoneHunt
{
    partial class Programa
    {
        public void AcharNome(string arquivo)
        {
            Carregar(); var sb = new StringBuilder();
            try
            {
                var clientes = Process.GetProcessesByName("client");
                sb.AppendLine("clientes: " + string.Join(", ", clientes.Select(x => x.Id.ToString())));
                if (clientes.Length == 0) { sb.AppendLine("nenhum client.exe aberto"); return; }
                foreach (var p in clientes)
                {
                    string erro; L.M = Memoria.Abrir(p, out erro);
                    if (L.M == null) { sb.AppendLine("PID " + p.Id + ": " + erro); continue; }
                    var st = L.LerStatus(); uint eu = L.NoJogador; var nos = L.NosMapa();
                    sb.AppendLine("PID " + p.Id + ": " + (st == null ? "vida/mana não lidas" : "nível " + st.Nivel + ", vida " + st.Vida + "/" + st.VidaMax) + ", jogadores no mapa: " + nos.Count);
                    foreach (var no in nos)
                    {
                        var b = L.M.Ler(no + O.Status, 4);
                        sb.AppendLine((no == eu ? " * " : "   ") + "nó 0x" + no.ToString("X") + (b != null ? " (nível " + BitConverter.ToInt32(b, 0) + ")" : ""));
                        for (int o = 0; o < 0x600; o += 4)
                        {
                            var s = L.M.QString(no + o, 40);
                            if (s != null && s.Length >= 2 && !s.Any(char.IsControl)) sb.AppendLine("       +0x" + o.ToString("X") + ": " + s);
                        }
                    }
                    L.M.Dispose(); L.M = null;
                }
            }
            catch (Exception e) { sb.AppendLine("erro: " + e.Message); }
            finally { File.WriteAllText(arquivo, sb.ToString(), Encoding.UTF8); }
        }
    }
}
#endif
