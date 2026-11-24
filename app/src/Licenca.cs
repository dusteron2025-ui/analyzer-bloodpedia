// Analyzer Bloodpedia — contagem de uso e trava de licença (para um plano pago no futuro).
//
// Contagem de uso: ao abrir (e a cada 6 h com o programa aberto), envia à Bloodpedia só um código aleatório desta
// instalação e a versão: POST https://www.bloodpedia.com.br/api/analyzer-uso { id, versao }. Serve para saber quantas
// pessoas usam o analyzer. Nada do personagem, da conta ou do computador é enviado.
//
// Trava: a resposta traz a "política" assinada pelo servidor (RSA-SHA256; aqui fica só a chave pública).
//   "livre"   → funciona normalmente (é o que vale hoje; sem assinatura também é "livre").
//   "licenca" → exige uma licença assinada, ligada a este computador e com validade (até 7 dias sem internet).
// A última política assinada fica guardada: tirar a internet não volta o programa para "livre".
// Uma licença ou política falsificada não passa na conferência da assinatura.
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BloodstoneHunt
{
    class ResultadoLicenca
    {
        public bool Bloqueado; public string Mensagem = "";
        public bool Licenciado; public DateTime? Vence;
    }

    static class Licenca
    {
        const string Servidor = "https://www.bloodpedia.com.br/api/";
        // chave pública (o par privado fica só no servidor, em ANALYZER_CHAVE_PRIVADA)
        const string Modulo = "wbvwIKUL+xyAQEITFmV6IV9EDWveihgCkpl0vMUqxD2d1b/l/RlzE6EG4WsTK2NS23khHTf1GucUv95goA2nCu5Exj6mdAQjXAWxbuFzyTkW9sDD9dTQ5ofocelV9cYsPNs87IwCm3p/zxjHox0aMwWQv3AM2szM895gE8sRHUNWVX+GuLyJusm87TBjDnuFMdsv/xKF8mTxbxHtrekBEHZRhJhanTcx3ne2Av7R8ek2yXTlhUdrp8+laGUMD0j6HzP4vmNskNyPIH6KrfsVq5FzqbDNn1ktFwuZ/1QrCDxXQnuuPl8KLi+bUljS3rUpkzzqQ5btX77LThgrdyEIRw==";
        const string Expoente = "AQAB";
        public const string PaginaPlanos = "https://www.bloodpedia.com.br/hunt-analyzer/";
        public const string PaginaPrivacidade = "https://www.bloodpedia.com.br/privacidade/#analyzer";

        static string Pasta { get { var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BloodstoneHunt"); Directory.CreateDirectory(p); return p; } }
        static string ArqId { get { return Path.Combine(Pasta, "id.txt"); } }
        static string ArqPolitica { get { return Path.Combine(Pasta, "politica.dat"); } }
        static string ArqLicenca { get { return Path.Combine(Pasta, "licenca.dat"); } }

        // ---------------- identificação ----------------
        /** código aleatório desta instalação (criado uma vez; não vem de nenhum dado do PC nem do jogo) */
        public static string Id()
        {
            try
            {
                if (File.Exists(ArqId)) { var t = File.ReadAllText(ArqId).Trim(); if (Regex.IsMatch(t, "^[a-f0-9]{32}$")) return t; }
                var n = Guid.NewGuid().ToString("N"); File.WriteAllText(ArqId, n); return n;
            }
            catch { return Guid.NewGuid().ToString("N"); }
        }
        /** código deste computador para a licença: hash do MachineGuid do Windows (não dá para voltar ao original) */
        static string Maquina()
        {
            string g = null;
            try { using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography")) g = k == null ? null : k.GetValue("MachineGuid") as string; } catch { }
            if (string.IsNullOrEmpty(g)) g = Environment.MachineName;
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("bloodpedia-analyzer|" + g.ToLowerInvariant())));
        }
        static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }

        // ---------------- servidor ----------------
        static string Post(string rota, string json, out int status)
        {
            status = 0;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create(Servidor + rota);
            req.Method = "POST"; req.ContentType = "application/json"; req.UserAgent = "AnalyzerBloodpedia/" + Info.Versao; req.Timeout = 8000; req.ReadWriteTimeout = 8000;
            var b = Encoding.UTF8.GetBytes(json);
            using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            HttpWebResponse r;
            try { r = (HttpWebResponse)req.GetResponse(); }
            catch (WebException e) { r = e.Response as HttpWebResponse; if (r == null) throw; }
            using (r) using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) { status = (int)r.StatusCode; return sr.ReadToEnd(); }
        }
        static string Campo(string json, string nome) { var m = Regex.Match(json ?? "", "\"" + nome + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""); return m.Success ? Regex.Unescape(m.Groups[1].Value) : null; }
        static long Numero(string json, string nome) { var m = Regex.Match(json ?? "", "\"" + nome + "\"\\s*:\\s*(\\d+)"); long v; return m.Success && long.TryParse(m.Groups[1].Value, out v) ? v : 0; }
        static string Assinado(string json, string objeto)
        {
            // "objeto": { "dados": "...", "assinatura": "..." } → devolve "dados|assinatura"
            var m = Regex.Match(json ?? "", "\"" + objeto + "\"\\s*:\\s*\\{([^}]*)\\}");
            if (!m.Success) return null;
            var d = Campo("{" + m.Groups[1].Value + "}", "dados"); var a = Campo("{" + m.Groups[1].Value + "}", "assinatura");
            return d != null && a != null ? d + "|" + a : null;
        }

        // ---------------- assinatura ----------------
        /** confere "dados|assinatura" e devolve o JSON assinado (ou null se não confere) */
        public static string Conferir(string par)
        {
            try
            {
                if (string.IsNullOrEmpty(par)) return null;
                var p = par.Split('|'); if (p.Length != 2) return null;
                var dados = Convert.FromBase64String(p[0]); var sig = Convert.FromBase64String(p[1]);
                return ConfereRsa(dados, sig, Modulo, Expoente) ? Encoding.UTF8.GetString(dados) : null;
            }
            catch { return null; }
        }
        /** assinatura RSA-SHA256 (PKCS#1 v1.5) de "dados" com a chave pública (módulo e expoente em base64) */
        public static bool ConfereRsa(byte[] dados, byte[] sig, string modulo, string expoente)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider(new CspParameters(24)))
                using (var sha = SHA256.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(modulo), Exponent = Convert.FromBase64String(expoente) });
                    return rsa.VerifyHash(sha.ComputeHash(dados), CryptoConfig.MapNameToOID("SHA256"), sig);
                }
            }
            catch { return false; }
        }
        static long Agora { get { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds; } }
        static string Ler(string arq) { try { return File.Exists(arq) ? File.ReadAllText(arq).Trim() : null; } catch { return null; } }
        static void Gravar(string arq, string t) { try { File.WriteAllText(arq, t); } catch { } }

        /** licença guardada: válida para esta instalação/máquina e ainda no prazo? */
        static string LicencaValida(string id, string maquina)
        {
            var j = Conferir(Ler(ArqLicenca)); if (j == null) return null;
            if (Campo(j, "maquina") != maquina) return null;
            long exp = Numero(j, "expira"), emi = Numero(j, "emitido");
            if (exp <= Agora || emi > Agora + 86400000L) return null;   // vencida ou relógio voltado para trás demais
            return j;
        }
        static DateTime? Data(long ms) { return ms > 0 ? (DateTime?)new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime() : null; }

        // ---------------- o que o programa chama ----------------
        /** conta o uso e decide se o analyzer pode rodar. Roda fora da tela (faz pedidos à internet). */
        public static ResultadoLicenca Checar(Action<string> log)
        {
            string id = Id(), maquina = Maquina(), modo = null;
            try
            {
                int st; var r = Post("analyzer-uso/", "{\"id\":\"" + id + "\",\"versao\":\"" + Info.Versao + "\"}", out st);
                if (st == 200)
                {
                    var par = Assinado(r, "politica"); var j = Conferir(par);
                    if (j != null && Campo(j, "id") == id) { Gravar(ArqPolitica, par); modo = Campo(j, "modo"); }
                    else if (Regex.IsMatch(r, "\"politica\"\\s*:\\s*null")) { modo = "livre"; try { File.Delete(ArqPolitica); } catch { } }
                }
                log("uso: resposta " + st + ", modo " + (modo ?? "?"));
            }
            catch (Exception e) { log("uso: sem resposta (" + e.Message + ")"); }
            // sem resposta: vale a última política assinada que ficou guardada
            if (modo == null) { var j = Conferir(Ler(ArqPolitica)); modo = j == null ? "livre" : Campo(j, "modo"); }
            if (modo != "licenca") return new ResultadoLicenca();

            // cobrança ligada: precisa de licença. Renova quando faltam menos de 2 dias (ou se não tiver).
            var lic = LicencaValida(id, maquina);
            if (lic == null || Numero(lic, "expira") - Agora < 2 * 86400000L)
            {
                string erro; bool ok = Renovar(id, maquina, null, out erro);
                lic = LicencaValida(id, maquina);
                if (lic == null && ok) return new ResultadoLicenca();   // o servidor voltou para "livre"
                if (lic == null) return new ResultadoLicenca { Bloqueado = true, Mensagem = erro ?? "Ative sua licença para usar o analyzer." };
            }
            return new ResultadoLicenca { Licenciado = true, Vence = Data(Numero(lic, "vence")) };
        }

        /** ativa com a chave digitada (ou renova sem chave). Devolve true se ficou com licença válida. */
        public static bool Renovar(string id, string maquina, string chave, out string erro)
        {
            erro = null;
            try
            {
                int st;
                var corpo = "{\"id\":\"" + id + "\",\"maquina\":\"" + maquina + "\"" + (chave != null ? ",\"chave\":\"" + Regex.Replace(chave, "[^A-Za-z0-9-]", "") + "\"" : "") + "}";
                var r = Post("analyzer-licenca/", corpo, out st);
                if (st == 200 && Campo(r, "modo") == "livre") { try { File.Delete(ArqPolitica); } catch { } return true; }
                var par = Assinado(r, "licenca"); var j = Conferir(par);
                if (st == 200 && j != null && Campo(j, "maquina") == maquina) { Gravar(ArqLicenca, par); return true; }
                erro = Campo(r, "erro") ?? "Não foi possível validar a licença agora.";
            }
            catch { erro = "Sem conexão com a Bloodpedia. Verifique a internet e tente de novo."; }
            return false;
        }
        public static bool Ativar(string chave, out string erro) { return Renovar(Id(), Maquina(), chave, out erro); }
    }
}
