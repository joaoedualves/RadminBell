// Tela do Guest: cola o convite do host, entra na rede do Radmin (passo a passo), aponta o jogo para o servidor
// dele e volta para o servidor de antes.
// No RadminBell ninguem digita login: quem entrou na rede do Radmin ja esta liberado, e a conta do jogo (que o
// shadNet exige) e criada sozinha, com o mesmo nome e uma senha aleatoria guardada so pro usuario do Windows.
// Na pasta tools do Seamless: account.dat = conta em uso; account-radmin.dat = conta do RadminBell;
// account-anterior.dat = servidor de antes, guardado na troca (o do launcher fica em %LOCALAPPDATA%\RadminBell).
// O RadminBell nao altera arquivos do jogo, do emulador nem do mod: so o account.dat (que o script oficial do BBSC
// le) e a pagina Online do launcher da BBPort.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RadminBell
{
    static class Switch
    {
        public static string Root;   // pasta do Seamless Co-op (a que tem tools\play-bbport.ps1)
        // sem pasta do Seamless (Bloodborne normal ou pasta nao achada) estes ficam null
        static string InTools(string f) { return Root == null || !Coop.Seamless ? null : Path.Combine(Root, "tools", f); }
        public static string Active { get { return InTools("account.dat"); } }
        public static string RadminFile { get { return InTools("account-radmin.dat") ?? Path.Combine(Paths.Data, "account-radmin.dat"); } }
        public static string PrevFile { get { return InTools("account-anterior.dat"); } }
        // fica no %LOCALAPPDATA%\RadminBell: sobrevive se a pessoa apagar a pasta do Seamless (o antigo ficava em tools\)
        static string PrevLauncher { get { return Path.Combine(Paths.Data, "launcher-anterior.json"); } }
        static string OldPrevLauncher { get { return InTools("launcher-anterior.json"); } }
        public static readonly string LauncherFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"bbport-launcher\settings.json");
        static readonly string[] OnlineKeys = { "online", "online_pool", "online_server", "online_webapi", "online_community", "online_npid", "online_password" };

        // senha do account.dat: o play-bbport.ps1 do BBSC le com ConvertTo-SecureString, entao tem que ser
        // o mesmo formato do ConvertFrom-SecureString (DPAPI do usuario, texto UTF-16, hex)
        public static string Protect(string s)
        {
            var enc = ProtectedData.Protect(Encoding.Unicode.GetBytes(s), null, DataProtectionScope.CurrentUser);
            return string.Concat(enc.Select(x => x.ToString("x2")));
        }
        public static string Unprotect(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "";   // servidor sem senha (ex.: The Hunter's Dream): nada pra descriptografar
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return Encoding.Unicode.GetString(ProtectedData.Unprotect(b, null, DataProtectionScope.CurrentUser));
        }

        // pasta do Seamless gravavel sem admin? (so pede o 'Sim' do Windows quando ela estiver em pasta protegida)
        public static bool CanWrite()
        {
            if (Root == null || !Coop.Seamless) return true;
            try
            {
                string t = Path.Combine(Root, "tools", ".radminbell-write-test");
                File.WriteAllText(t, ""); File.Delete(t); return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return true; }   // outro erro: deixa a acao mostrar a mensagem real
        }

        public static bool IsSeamless(string d) { return !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, @"tools\play-bbport.ps1")); }

        // pasta guardada; senao a unica pasta irma com tools\play-bbport.ps1; senao pergunta (seletor do Windows)
        public static bool FindRoot(bool ask)
        {
            string r = Paths.ReadLine1(Paths.SeamlessFile);
            if (!ask && IsSeamless(r)) { Root = r; return true; }
            if (!ask)
            {
                try
                {
                    var found = Directory.GetDirectories(Path.GetDirectoryName(Paths.AppDir)).Where(IsSeamless).ToList();
                    if (found.Count == 1) { Root = found[0]; Paths.WriteText(Paths.SeamlessFile, Root); return true; }
                }
                catch { }
                return false;   // ao abrir nao pergunta: so pede a pasta quando precisar
            }
            // a pessoa escolhe a PASTA do Seamless Co-op (ou uma acima/dentro dela): o RadminBell acha o resto sozinho
            string sel = FolderPicker.Pick(Form.ActiveForm, "RadminBell - " + L.T("escolha a pasta do Seamless Co-op", "pick the Seamless Co-op folder"));
            if (sel == null) return false;
            var hits = FolderPicker.FindWith(sel, @"tools\play-bbport.ps1");
            if (hits.Count == 0)
            {
                if (Msg.Ask(null, L.T("O Seamless Co-op não foi encontrado nessa pasta (nem dentro dela).\n\nO RadminBell só troca o servidor: precisa do Seamless Co-op (BBSC) da BBport já instalado e funcionando.\n\nAbrir a página de download do Seamless Co-op?",
                                      "Seamless Co-op was not hits in that folder (or inside it).\n\nRadminBell only switches the server: you need Seamless Co-op (BBSC) for BBport already installed and working.\n\nOpen the Seamless Co-op download page?"),
                              L.T("Abrir página", "Open page"), L.T("Fechar", "Close")))
                    Util.OpenUrl("https://github.com/CLSAMG22/BBSC/releases");
                return false;
            }
            Root = hits[0]; Paths.WriteText(Paths.SeamlessFile, Root);
            if (hits.Count > 1)
                Msg.Show(null, L.T("Foram encontradas " + hits.Count + " pastas do Seamless Co-op. Ficou esta:\n" + Root + "\n\nSe não for a que você usa, escolha a pasta dela diretamente.",
                                   hits.Count + " Seamless Co-op folders were hits. Using this one:\n" + Root + "\n\nIf it is not the one you use, pick that folder directly."));
            return true;
        }

        // Bloodborne normal: a pasta da BBPort (a que tem o Bloodborne.exe), para o botao Abrir o jogo
        public static string BBPortDir()
        {
            string d = Paths.ReadLine1(Paths.BBPortFile);
            return d != "" && File.Exists(Path.Combine(d, "Bloodborne.exe")) ? d : null;
        }
        public static bool FindBBPort()
        {
            string sel = FolderPicker.Pick(Form.ActiveForm, "RadminBell - " + L.T("escolha a pasta do Bloodborne (BBPort, a que tem o Bloodborne.exe)", "pick the Bloodborne folder (BBPort, the one with Bloodborne.exe)"));
            if (sel == null) return false;
            var found = FolderPicker.FindWith(sel, "Bloodborne.exe");
            if (found.Count == 0)
            {
                Msg.Show(null, L.T("O Bloodborne.exe não foi encontrado nessa pasta (nem dentro dela). Escolha a pasta da BBPort.", "Bloodborne.exe was not found in that folder (or inside it). Pick the BBPort folder."));
                return false;
            }
            Paths.WriteText(Paths.BBPortFile, found[0]);
            return true;
        }

        public static Dictionary<string, object> Account(string f) { return f == null ? null : Json.Read(f); }
        public static string Srv(Dictionary<string, object> a) { return Json.Str(a, "server") ?? ""; }
        public static bool IsRadmin(Dictionary<string, object> a) { return a != null && Srv(a).StartsWith("26."); }   // rede do Radmin VPN = 26.x
        public static string HostOf(Dictionary<string, object> a) { return Srv(a).Split(':')[0]; }

        // ---- pagina Online do launcher da BBport ----
        static void WriteLauncher(Dictionary<string, object> cfg)
        {
            if (File.Exists(LauncherFile)) File.Copy(LauncherFile, LauncherFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            Directory.CreateDirectory(Path.GetDirectoryName(LauncherFile));
            Json.Write(LauncherFile, cfg);
        }
        static void SetLauncherOnline(Dictionary<string, object> acc)
        {
            var cfg = Json.Read(LauncherFile) ?? new Dictionary<string, object>();
            var p = Srv(acc).Split(':'); string addr = p[0], port = p.Length > 1 ? p[1] : "31313";
            cfg["online"] = true; cfg["online_pool"] = "shadnet"; cfg["online_server"] = addr + ":" + port;
            cfg["online_webapi"] = "http://" + addr + ":31315"; cfg["online_community"] = "http://" + addr;
            cfg["online_npid"] = Json.Str(acc, "name"); cfg["online_password"] = Unprotect(Json.Str(acc, "password"));
            WriteLauncher(cfg);
        }
        // para onde o jogo vai entrar (lido do launcher)
        public static string LauncherServer()
        {
            var cfg = Json.Read(LauncherFile);
            if (cfg == null) return null;
            object on; if (cfg.TryGetValue("online", out on) && on is bool && !(bool)on) return "offline";
            string pool = Json.Str(cfg, "online_pool") ?? "";
            if (pool == "huntersdream") return "The Hunter's Dream";
            if (pool == "supermedo") return "SuperMedo Server";
            return Json.Str(cfg, "online_server");
        }

        // guarda o servidor atual (qualquer um que nao seja Radmin) antes de ir pro RadminBell
        static void SaveAnterior(Dictionary<string, object> cur)
        {
            if (IsRadmin(cur)) return;
            if (cur != null && Active != null) File.Copy(Active, PrevFile, true);
            var cfg = Json.Read(LauncherFile);
            if (cfg != null && (Json.Str(cfg, "online_server") ?? "").StartsWith("26.")) return;   // launcher ja no RadminBell: nao guarda o nosso como "anterior"
            var saved = new Dictionary<string, object>();
            foreach (var k in OnlineKeys)
            {
                object v = null;
                if (cfg != null) cfg.TryGetValue(k, out v);
                if (k == "online_password" && v != null && v.ToString() != "") v = Protect(v.ToString());   // nao fica senha aberta neste arquivo
                saved[k] = v;
            }
            Json.Write(PrevLauncher, saved);
        }
        // texto embaixo do "Voltar": o servidor que estava no launcher antes (e o que realmente muda)
        public static string PrevLabel()
        {
            var saved = Json.Read(PrevLauncher) ?? Json.Read(OldPrevLauncher);
            if (saved != null)
            {
                object on; if (saved.TryGetValue("online", out on) && on is bool && !(bool)on) return L.T("offline (sem online)", "offline (no online)");
                string pool = Json.Str(saved, "online_pool") ?? "";
                if (pool == "huntersdream") return "The Hunter's Dream";
                if (pool == "supermedo") return "SuperMedo Server";
                var s = Json.Str(saved, "online_server");
                if (!string.IsNullOrEmpty(s)) return s;
            }
            var prev = Account(PrevFile);
            return prev != null ? Srv(prev) : null;
        }

        // desfaz tudo que o RadminBell mudou: volta o servidor guardado; sem ele, tira a conta e a pagina Online que a gente gravou
        public static string UseAnterior(out bool ok)
        {
            ok = false;
            if (Util.GameOpen())
                return L.T("Feche o jogo e o launcher da BBPort antes (senão ele grava as configurações de novo ao fechar).",
                           "Close the game and the BBPort launcher first (otherwise it writes the settings back when it closes).");
            ok = true;
            string label = PrevLabel();
            var cur = Account(Active);
            var prev = Account(PrevFile);
            bool changed = false;
            if (prev != null) { File.Copy(PrevFile, Active, true); changed = true; }
            else if (IsRadmin(cur)) { File.Delete(Active); changed = true; }

            var cfg = Json.Read(LauncherFile);
            var saved = Json.Read(PrevLauncher) ?? Json.Read(OldPrevLauncher);
            if (cfg != null)
            {
                // qualquer endereco 26.x (servidor, WebAPI ou community) = gravado pelo RadminBell;
                // o login do The Hunter's Dream usa o online_community: se ficar http://26.x ele nao conecta
                bool ours = new[] { "online_server", "online_webapi", "online_community" }.Any(k => (Json.Str(cfg, k) ?? "").Replace("http://", "").StartsWith("26."));
                if (saved != null && new[] { "online_server", "online_webapi", "online_community" }.Any(k => (Json.Str(saved, k) ?? "").Replace("http://", "").StartsWith("26.")))
                    saved = null;   // copia "anterior" que ja era do RadminBell: nao serve pra voltar
                if (saved != null)
                {
                    foreach (var k in OnlineKeys)
                    {
                        object v; saved.TryGetValue(k, out v);
                        if (v == null) { cfg.Remove(k); continue; }
                        if (k == "online_password") v = Unprotect(v.ToString());
                        cfg[k] = v;
                    }
                }
                else if (ours) foreach (var k in OnlineKeys) cfg.Remove(k);
                if (saved != null || ours) { WriteLauncher(cfg); changed = true; }
            }

            foreach (var f in new[] { PrevFile, PrevLauncher, OldPrevLauncher, RadminFile })
                if (f != null && File.Exists(f)) { File.Delete(f); changed = true; }

            if (changed && label != null)
                return L.T("Pronto: voltou para o servidor de antes (" + label + ") e o que o RadminBell tinha mudado foi apagado.\nAbra o jogo normalmente, como antes.",
                           "Done: back on your previous server (" + label + ") and RadminBell's changes were removed.\nStart the game as usual, like before.");
            if (changed)
                return L.T("Pronto: tudo que o RadminBell tinha mudado foi apagado (conta e página Online do launcher).\nAbra o launcher e entre no seu servidor normalmente (se pedir, faça login de novo).",
                           "Done: everything RadminBell changed was removed (account and the launcher Online page).\nOpen the launcher and join your server as usual (log in again if it asks).");
            return L.T("Nada pra desfazer: o RadminBell não tinha mudado nada aqui.", "Nothing to undo: RadminBell had not changed anything here.");
        }

        // reset: tira o settings.json do launcher do caminho (vira settings.json.radminbell); a BBPort cria um novo sozinha
        public static string ResetLauncher(out bool ok)
        {
            ok = false;
            if (Util.GameOpen())
                return L.T("Feche o jogo e o launcher da BBPort antes (senão ele grava as configurações de novo ao fechar).",
                           "Close the game and the BBPort launcher first (otherwise it writes the settings back when it closes).");
            if (!File.Exists(LauncherFile))
                return L.T("O launcher já está zerado (não tem settings.json).", "The launcher is already reset (no settings.json).");
            string bak = LauncherFile + ".radminbell";
            if (File.Exists(bak)) File.Delete(bak);
            File.Move(LauncherFile, bak);
            ok = true;
            return L.T("Pronto: o settings.json do launcher virou settings.json.radminbell.\nAbra o launcher: ele cria as configurações do zero (refaça o login no The Hunter's Dream / Discord).",
                       "Done: the launcher settings.json was renamed to settings.json.radminbell.\nOpen the launcher: it creates fresh settings (log in again to The Hunter's Dream / Discord).");
        }

        // shadnet-sample.exe (cria a conta no servidor): vem no Seamless Co-op (account\); senao o da pasta server ao lado do exe
        static string Tool()
        {
            foreach (var d in new[] { Root, Paths.ReadLine1(Paths.SeamlessFile) })
                if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, @"account\shadnet-sample.exe"))) return Path.Combine(d, @"account\shadnet-sample.exe");
            return Path.Combine(Paths.Server, "shadnet-sample.exe");
        }
        static string RunTool(string tool, string args)
        {
            var psi = new ProcessStartInfo(tool, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(tool) };
            using (var p = Process.Start(psi))
            {
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } }
                return o.Result + e.Result;
            }
        }

        // roda fora da thread da janela; devolve a mensagem pro usuario
        public static string UseRadmin(string ip, out bool ok)
        {
            ok = false;
            ip = ip.Trim();
            if (!Regex.IsMatch(ip, @"^26\.\d{1,3}\.\d{1,3}\.\d{1,3}$"))
                return L.T("Coloque o IP do servidor no Radmin (começa com 26., está no convite).", "Type the server Radmin IP (starts with 26., it is in the invite).");
            if (Coop.Seamless && Root == null)
                return L.T("A pasta do Seamless Co-op não foi encontrada (link 'Pasta do Seamless Co-op').", "The Seamless Co-op folder was not found ('Seamless Co-op folder' link).");
            // o launcher grava a pagina Online de novo quando fecha: com ele aberto a troca seria desfeita
            if (Util.GameOpen())
                return L.T("Feche o jogo e o launcher da BBPort e clique de novo.\n(Com o launcher aberto, ele desfaz a troca quando fecha.)",
                           "Close the game and the BBPort launcher and click again.\n(With the launcher open, it undoes the switch when it closes.)");
            var st = Radmin.Read();
            if (!st.PowerOn || !st.Ip.StartsWith("26."))
                return L.T("Radmin VPN desligado.\n\n1. Instale/abra o Radmin VPN (radmin-vpn.com) e ligue\n2. Entre na rede do servidor (nome e senha estão no convite)\n3. Clique aqui de novo.",
                           "Radmin VPN is off.\n\n1. Install/open Radmin VPN (radmin-vpn.com) and turn it on\n2. Join the server network (name and password are in the invite)\n3. Click here again.");
            if (!Util.HttpOk("http://" + ip + ":31315/status", 4000))
                return L.T("O servidor (" + ip + ") não respondeu.\nEle precisa estar ligado e você na rede dele no Radmin.", "The server (" + ip + ") did not answer.\nIt must be on and you must be in its Radmin network.");
            Paths.WriteText(Paths.HostFile, ip);

            var cur = Account(Active);
            var acc = Account(RadminFile);
            // conta ja feita neste servidor: confere o login (servidor reinstalado/zerado = conta some ou muda;
            // o jogo mostraria "nao conectado a PlayStation Network")
            if (acc != null && Srv(acc) == ip + ":31313" && File.Exists(Tool()))
            {
                string lo = RunTool(Tool(), ip + " 31313 login " + Json.Str(acc, "name") + " " + Unprotect(Json.Str(acc, "password")));
                if (lo.Contains("[login] FAILED")) acc = null;
            }
            if (acc == null || Srv(acc) != ip + ":31313")
            {
                string tool = Tool();
                if (!File.Exists(tool))
                    return L.T("Falta o shadnet-sample.exe (cria a conta no servidor): ele vem na pasta account do Seamless Co-op.", "shadnet-sample.exe is missing (it creates the server account): it comes in the Seamless Co-op account folder.");
                // conta automatica: mesmo nome da conta atual (ou do launcher, ou do Windows) + senha aleatoria
                var prev = Account(PrevFile);
                var cfg0 = Json.Read(LauncherFile);
                string b = cur != null && !IsRadmin(cur) ? Json.Str(cur, "name") : prev != null ? Json.Str(prev, "name")
                         : !string.IsNullOrEmpty(Json.Str(cfg0, "online_npid")) ? Json.Str(cfg0, "online_npid") : Environment.UserName;
                b = Regex.Replace(b ?? "", "[^A-Za-z0-9_-]", "");
                if (b.Length < 3) b = "cacador" + b;
                if (b.Length > 13) b = b.Substring(0, 13);
                string pw = Util.RandomHex(12), name = null, outp = "";
                var rnd = new Random();
                for (int i = 0; i < 6; i++)
                {
                    string n = i == 0 ? b : b + rnd.Next(10, 999);
                    outp = RunTool(tool, ip + " 31313 register " + n + " " + pw + " " + n + "@bbsc.local");
                    if (outp.Contains("Account created successfully")) { name = n; break; }
                    if (!outp.Contains("CreationExisting")) break;
                }
                if (name == null)
                {
                    var lst = outp.Trim().Split('\n').Select(x => x.Trim()).Where(x => x != "").Reverse().Take(3).Reverse();
                    return L.T("O servidor não criou a conta:", "The server did not create the account:") + "\n" + string.Join("\n", lst);
                }
                acc = new Dictionary<string, object> { { "server", ip + ":31313" }, { "name", name }, { "password", Protect(pw) } };
                Json.Write(RadminFile, acc);
            }
            SaveAnterior(cur);
            if (Active != null) File.Copy(RadminFile, Active, true);
            SetLauncherOnline(acc);

            // confere o que ficou gravado (le de volta)
            var chk = Json.Read(LauncherFile);
            if (Json.Str(chk, "online_server") != ip + ":31313" || (Active != null && Srv(Account(Active)) != ip + ":31313"))
                return L.T("A troca não ficou gravada (o launcher ou outro programa mudou a configuração). Feche o launcher e tente de novo.",
                           "The switch was not saved (the launcher or another program changed the settings). Close the launcher and try again.");
            Guard();   // arquivos de redirecionamento antigos (outro servidor) passam a apontar para este
            ok = true;
            string nm = Json.Str(acc, "name");
            return Coop.Seamless
                ? L.T("Pronto: seu jogo vai entrar no servidor " + ip + " (seu nome: " + nm + ").", "Done: your game will join server " + ip + " (your name: " + nm + ").")
                : L.T("Pronto: o launcher da BBPort aponta para o servidor " + ip + " (seu nome: " + nm + "). Abra o jogo pelo launcher normal.",
                      "Done: the BBPort launcher points to server " + ip + " (your name: " + nm + "). Start the game with the regular launcher.");
        }

        // o jogo do Seamless: o .cmd oficial do BBSC (le o account.dat e abre o launcher da BBPort com o mod)
        public static bool OpenGame()
        {
            if (!Coop.Seamless)
            {
                // Bloodborne normal: o launcher da BBPort (Bloodborne.exe) - ele ja le a pagina Online gravada pelo RadminBell
                string d = BBPortDir();
                if (d == null && FindBBPort()) d = BBPortDir();
                if (d == null) return false;
                try { Process.Start(new ProcessStartInfo(Path.Combine(d, "Bloodborne.exe")) { WorkingDirectory = d, UseShellExecute = true }).Dispose(); return true; } catch { return false; }
            }
            if (Root == null) return false;
            string cmd = Path.Combine(Root, "OPEN BBPORT LAUNCHER + BBSC.cmd");
            if (!File.Exists(cmd)) return false;
            try { Process.Start(new ProcessStartInfo(cmd) { WorkingDirectory = Root, UseShellExecute = true }).Dispose(); return true; } catch { return false; }
        }

        // jogo aberto: le no log do mod em qual servidor ele conectou (linha RELAY). null = sem informacao
        public static string ModRelay()
        {
            if (!Coop.Seamless || Root == null) return null;
            try
            {
                var dir = new DirectoryInfo(Path.Combine(Root, @"bbsc\logs"));
                if (!dir.Exists) return null;
                var procs = Process.GetProcessesByName("bb-probe");
                DateTime started = DateTime.MaxValue;
                foreach (var p in procs) { try { if (p.StartTime < started) started = p.StartTime; } catch { } p.Dispose(); }
                if (procs.Length == 0) return null;
                var f = dir.GetFiles("bbsc-timeline-*.txt").OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                if (f == null || f.LastWriteTime < started.AddSeconds(-30)) return "";
                string text;
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                var m = Regex.Matches(text, @"RELAY\s+-\s+\|\s+connected to (\d+\.\d+\.\d+\.\d+)");
                return m.Count > 0 ? m[m.Count - 1].Groups[1].Value : "";
            }
            catch { return null; }
        }

        // ---- guarda: o jogo continua apontando para o servidor do RadminBell ----
        // Arquivos host_overrides*.json de instalacoes antigas (ex.: outro servidor) fazem o mod ir para outro
        // lugar quando o jogo e aberto por fora do .cmd (caso real: jogo.cacadoresbr.com). Enquanto o app esta
        // aberto e o RadminBell esta em uso (account-radmin.dat existe), confere e corrige: arquivos de
        // redirecionamento, conta (account.dat) e pagina Online do launcher. Saves nao sao tocados.
        static readonly object guardLock = new object();
        static string OverridesJson(string a)
        {
            return "{\r\n  \"https://ss4.scej-network.jp:20443\": \"http://" + a + ":20443\",\r\n" +
                   "  \"http://thehuntersdream.com:18671/summon_messenger\": \"http://" + a + ":31315\",\r\n" +
                   "  \"https://thehuntersdream.com:18671/summon_messenger\": \"http://" + a + ":31315\"\r\n}\r\n";
        }
        // pastas onde o mod/emulador procura o host_overrides (ver bbsc_http.cpp HostOverridesFiles)
        static List<string> OverrideFiles()
        {
            var res = new List<string>();
            // pastas do Seamless Co-op: qualquer host_overrides (inclusive dentro de savedata)
            if (Coop.Seamless && Root != null)
                foreach (var d in new[] { "user", @"bbsc\user", @"emulator\user", @"emulator-novo\user" })
                {
                    string p = Path.Combine(Root, d);
                    if (Directory.Exists(p)) try { res.AddRange(Directory.GetFiles(p, "host_overrides*.json", SearchOption.AllDirectories)); } catch { }
                }
            // pasta user da BBport: so o host_overrides.custom.json (o que o jogo usa). O host_overrides.json dela e
            // o padrao da propria BBport (The Hunter's Dream), so vale sem o custom - nao e mexido.
            string bbport = null;
            if (Coop.Seamless && Root != null)
            {
                var bp = Json.Str(Json.Read(Path.Combine(Root, @"tools\bbport-paths.json")), "bbport");
                if (!string.IsNullOrEmpty(bp)) bbport = Path.GetDirectoryName(bp);
            }
            else bbport = BBPortDir();
            if (bbport != null)
            {
                string c = Path.Combine(bbport, @"user\host_overrides.custom.json");
                if (File.Exists(c)) res.Add(c);
            }
            return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        static bool PointsTo(string file, string addr)
        {
            var j = Json.Read(file);
            if (j == null) return false;
            foreach (var v in j.Values)
            {
                var s = v as string;
                if (s == null) continue;
                try { if (!string.Equals(new Uri(s).Host, addr, StringComparison.OrdinalIgnoreCase)) return false; } catch { return false; }
            }
            return true;
        }
        static void GuardLog(string t)
        {
            try { File.AppendAllText(Path.Combine(Paths.Data, "correcoes.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + t + "\r\n", new UTF8Encoding(false)); } catch { }
        }
        // devolve quantas coisas corrigiu (0 = estava tudo certo ou o RadminBell nao esta em uso)
        public static int Guard()
        {
            lock (guardLock)
            {
                int fixes = 0;
                try
                {
                    var acc = Account(RadminFile);
                    if (!IsRadmin(acc)) return 0;   // RadminBell fora de uso (Voltar apaga o account-radmin.dat)
                    string addr = HostOf(acc);
                    bool game = Util.GameOpen();
                    // 1. conta do Seamless (account.dat) - so com o jogo fechado (o .cmd le na abertura)
                    if (Active != null && !game && Srv(Account(Active)) != Srv(acc)) { File.Copy(RadminFile, Active, true); fixes++; GuardLog("account.dat voltou para " + Srv(acc)); }
                    // 2. pagina Online do launcher - com o launcher fechado (aberto, ele regrava ao fechar; confere de novo depois)
                    if (!game)
                    {
                        var cfg = Json.Read(LauncherFile);
                        // sem settings.json (Reset do launcher): a BBPort cria um novo; a guarda confere depois
                        if (cfg != null && Json.Str(cfg, "online_server") != Srv(acc) || Json.Str(cfg, "online_webapi") != "http://" + addr + ":31315"
                            || Json.Str(cfg, "online_community") != "http://" + addr || Json.Str(cfg, "online_npid") != Json.Str(acc, "name"))
                        { SetLauncherOnline(acc); fixes++; GuardLog("launcher (pagina Online) voltou para " + Srv(acc)); }
                    }
                    // 3. arquivos de redirecionamento apontando para outro servidor
                    foreach (var f in OverrideFiles())
                    {
                        if (PointsTo(f, addr)) continue;
                        string bak = f + ".antes-radminbell";
                        if (!File.Exists(bak)) File.Copy(f, bak);
                        File.WriteAllText(f, OverridesJson(addr), new UTF8Encoding(false));
                        fixes++; GuardLog(f + " apontava para outro servidor; agora " + addr + " (original em " + Path.GetFileName(bak) + ")");
                    }
                }
                catch (Exception e) { GuardLog("erro: " + e.Message); }
                return fixes;
            }
        }
    }

    class GuestForm : DarkForm
    {
        readonly StatusBox status = new StatusBox();
        readonly DarkInput ipBox = new DarkInput();
        readonly DarkButton go, back;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 4000 };
        bool busy, refreshing, answering, searching, badInvite;
        bool autoPicked;   // conexao iniciada pela janela "Redes encontradas" (se falhar, pede para colocar manualmente)
        string checkedIp = "", launcherSrv;
        List<string> players;
        List<string> foundServers;
        RadminState last;
        Invite invite;

        public GuestForm() : base(450, "Guest")
        {
            int pad = D(22), w = ClientSize.Width - pad * 2, y = ContentTop + D(14);

            var role = new RoleTitle("guest") { Location = new Point(pad, y), Size = new Size(w, D(30)) };
            Controls.Add(role); y = role.Bottom + D(12);

            status.Location = new Point(pad, y); status.Size = new Size(w, D(10) * 2 + status.LineH * 3);
            Controls.Add(status); y = status.Bottom + D(8);

            var note = new Label { Text = L.T("Todos do grupo precisam estar no MESMO servidor.", "Everyone in the group must be on the SAME server."),
                                   Font = Theme.Body(8.5f), ForeColor = Theme.Hex("#5a5a5a"), BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleCenter,
                                   Location = new Point(pad, y), Size = new Size(w, D(20)) };
            Controls.Add(note); y = note.Bottom + D(10);

            var openRadmin = new DarkButton { Text = L.T("Abrir Radmin VPN", "Open Radmin VPN"), Glyph = "", Location = new Point(pad, y), Size = new Size(w, D(44)) };
            openRadmin.Click += (s, e) =>
            {
                if (!Radmin.Open()) Msg.Show(this, L.T("Radmin VPN não está instalado. O site dele foi aberto: instale e clique aqui de novo.",
                                                     "Radmin VPN is not installed. Its website was opened: install it and click here again."));
            };
            Controls.Add(openRadmin); y = openRadmin.Bottom + D(22);

            // ---- cartao do servidor ----
            var card = new Card { Title = L.T("Servidor RadminBell", "RadminBell server"), TitleH = D(20), Location = new Point(pad, y) };
            Controls.Add(card);
            int cp = D(16), cw = w - cp * 2, cy = card.TitleH + D(12);
            card.Controls.Add(new Label { Text = L.T("IP do servidor", "Server IP").ToUpperInvariant(), Font = Theme.Body(7.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(cp, cy), Size = new Size(cw, D(16)) });
            cy += D(18);
            int h = D(36);
            ipBox.Placeholder = L.T("cole o convite abaixo ou escreva o IP (26.x)", "paste the invite below or type the IP (26.x)");
            ipBox.Location = new Point(cp, cy); ipBox.Size = new Size(cw, h);
            card.Controls.Add(ipBox); cy += h + D(10);
            var paste = new DarkButton { Text = L.T("Colar convite do amigo", "Paste friend's invite"), Glyph = "", Style = BtnStyle.Gold, Location = new Point(cp, cy), Size = new Size(cw, D(40)) };
            paste.Click += (s, e) => PasteInvite();
            card.Controls.Add(paste); cy = paste.Bottom + D(10);
            go = new DarkButton { Text = L.T("Conectar", "Connect"), Glyph = "", Style = BtnStyle.Primary, Location = new Point(cp, cy), Size = new Size(cw, D(44)) };
            go.Click += (s, e) => DoConnect();
            card.Controls.Add(go); cy = go.Bottom + D(16);
            card.Size = new Size(w, cy); y = card.Bottom + D(18);

            Controls.Add(new Divider { Location = new Point(pad, y), Size = new Size(w, D(9)) }); y += D(9) + D(16);

            back = new DarkButton { Text = L.T("Voltar ao meu servidor anterior", "Back to my previous server"), Glyph = "", Location = new Point(pad, y), Size = new Size(w, D(52)) };
            back.Click += (s, e) => DoBack();
            Controls.Add(back); y = back.Bottom + D(8);

            var reset = new DarkButton { Text = L.T("Reset do launcher", "Reset launcher"), Glyph = "", Style = BtnStyle.Small, Font = Theme.Body(8.5f), Location = new Point(pad + w / 4, y), Size = new Size(w / 2, D(28)) };
            reset.Click += (s, e) => DoReset();
            Controls.Add(reset); y = reset.Bottom + D(16);

            y += Footer.Add(this, pad, w, y, false);
            FinishHeight(y + D(10));

            invite = Invite.Saved();
            ipBox.Text = DefaultIp();
            ipBox.Box.TextChanged += (s, e) =>
            {
                answering = false; checkedIp = ""; players = null;
                string t = ipBox.Text.Trim();
                // codigo de convite colado direto no campo: le e troca pelo IP do host
                if (t.Contains("RB1-"))
                {
                    var inv = Invite.Parse(t);
                    badInvite = inv == null;
                    if (inv != null) { invite = inv; invite.Save(); ipBox.Text = inv.Ip; return; }
                }
                else if (t != "") badInvite = false;
                Draw();
            };
            ipBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoConnect(); } };
            timer.Tick += (s, e) => Refresh_();
            Draw();   // altura final antes de centralizar (senao a janela "nasce" para baixo)
            Load += (s, e) => { var a = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(a.Left + (a.Width - Width) / 2, a.Top + Math.Max(0, (a.Height - Height) / 2)); };
            Shown += (s, e) => { Draw(); Refresh_(); timer.Start(); ActiveControl = null; if (ipBox.Text.Trim() == "") Search(false); };
        }

        // IP do host para o campo: nunca o IP do proprio PC (o Host deste PC grava o IP dele mesmo)
        string DefaultIp()
        {
            string own = Radmin.Read().Ip;
            if (invite != null && invite.Ip != "" && invite.Ip != own) return invite.Ip;
            var cur = Switch.Account(Switch.Active);
            string acc = Switch.IsRadmin(cur) ? Switch.HostOf(cur) : "";
            if (acc != "" && acc != own) return acc;
            string ip = Paths.ReadLine1(Paths.HostFile);
            return ip != "" && ip != own ? ip : "";
        }
        string OwnIp { get { return last != null ? last.Ip : ""; } }
        bool IsOwn(string ip) { return ip != "" && ip == OwnIp; }

        // procura servidores RadminBell ligados na rede do Radmin; com um so, ja preenche o IP
        void Search(bool tell)
        {
            if (searching) return;
            searching = true; Draw();
            string own = last != null ? last.Ip : Radmin.Read().Ip;
            // 1o pergunta "quem e host?" pela rede do Radmin (vem IP e nome da rede); sem resposta, tenta pelos IPs ja vistos
            Task.Run(() =>
            {
                var d = Finder.Discover(own);
                if (d.Count == 0) d = Finder.Servers(own).Select(x => new KeyValuePair<string, string>(x, "")).ToList();
                return d;
            }).ContinueWith(t =>
            {
                searching = false;
                if (IsDisposed) return;
                var found = t.IsFaulted ? new List<KeyValuePair<string, string>>() : t.Result.Where(x => !IsOwn(x.Key)).ToList();
                // host respondeu sem o nome da rede: se o guest esta numa rede so do Radmin, e ela
                var mine = (last ?? Radmin.Read()).Networks.ToList();
                found = found.Select(x => x.Value == "" && mine.Count == 1 ? new KeyValuePair<string, string>(x.Key, mine[0]) : x).ToList();
                // + hosts em que este PC ja se conectou pelo app, numa rede que ainda esta no Radmin (mesmo com o servidor desligado agora)
                foreach (var n in SavedNets.Names())
                {
                    string hip = SavedNets.HostIp(n);
                    if (!string.IsNullOrEmpty(hip) && mine.Contains(n) && !IsOwn(hip) && !found.Any(x => x.Key == hip))
                        found.Add(new KeyValuePair<string, string>(hip, n));
                }
                Draw();
                if (found.Count > 0 && ipBox.Text.Trim() == "")
                {
                    // "Redes encontradas": so o nome da rede (igual ao Host); o IP so aparece se faltar o nome ou repetir a rede
                    var items = found.Select(x => x.Value == "" ? x.Key : found.Count(y => y.Value == x.Value) > 1 ? x.Value + "   " + x.Key : x.Value).ToList();
                    Activate();   // app na frente antes da janela de redes (senao ela nasce atras)
                    int i = NetPicker.Ask(this, items, L.T("Inserir convite", "Enter invite"));
                    if (i >= 0)
                    {
                        invite = new Invite { Ip = found[i].Key, Net = found[i].Value };
                        invite.Save();
                        ipBox.Text = found[i].Key;
                        autoPicked = true;
                        DoConnect();
                        return;
                    }
                    ipBox.Box.Focus();
                }
                else if (tell && found.Count == 0)
                    Msg.Show(this, L.T("Nenhum servidor RadminBell ligado foi encontrado na sua rede do Radmin.\nConfira se você entrou na rede do host e se ele clicou em Ligar.",
                                       "No RadminBell server is on in your Radmin network.\nCheck that you joined the host's network and that the host clicked Start."));
                Refresh_();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void PasteInvite()
        {
            var inv = Invite.Parse(Clipboard.ContainsText() ? Clipboard.GetText() : "");
            badInvite = inv == null;
            if (inv == null)
            {
                Draw();
                Msg.Show(this, L.T("Não foi encontrado um convite no que foi copiado. Copie o convite que o host mandou (começa com RB1-) e clique de novo.",
                                   "No invite found in what you copied. Copy the invite the host sent (it starts with RB1-) and click again."));
                return;
            }
            if (inv.Net == "" && invite != null && invite.Ip == inv.Ip) inv = invite;   // so o IP: mantem rede/senha do convite anterior
            invite = inv; invite.Save();
            ipBox.Text = inv.Ip;
            if (ModMismatch())
                Msg.Show(this, L.T("ATENÇÃO: vocês não estão com a mesma versão do mod.\n\nHost: " + ModInfo.Label(invite.Mod, invite.ModVer) + "\nVocê: " + ModInfo.Label(ModInfo.Hash(), ModInfo.Version()) + "\n\nCom versões diferentes vocês não se enxergam no jogo. Peça ao host o mesmo pacote.",
                                   "WARNING: you are not on the same mod version.\n\nHost: " + ModInfo.Label(invite.Mod, invite.ModVer) + "\nYou: " + ModInfo.Label(ModInfo.Hash(), ModInfo.Version()) + "\n\nWith different versions you can't see each other in game. Ask the host for the same package."));
            Draw(); Refresh_();
        }
        bool ModMismatch()
        {
            if (invite == null || string.IsNullOrEmpty(invite.Mod)) return false;
            string mine = ModInfo.Hash();
            return mine != "" && mine != invite.Mod;
        }

        void Refresh_()
        {
            if (refreshing || busy || !Visible) return;
            refreshing = true;
            string ip = ipBox.Text.Trim();
            Task.Run(() =>
            {
                var st = Radmin.Read();
                bool valid = Regex.IsMatch(ip, @"^26\.\d{1,3}\.\d{1,3}\.\d{1,3}$");
                bool ans = valid && st.PowerOn && Util.HttpOk("http://" + ip + ":31315/status", 2500);
                var pl = ans ? Players.Fetch(ip) : null;
                return Tuple.Create(st, ans, ip, pl, Switch.LauncherServer());
            }).ContinueWith(t =>
            {
                refreshing = false;
                if (t.IsFaulted || IsDisposed) return;
                last = t.Result.Item1; launcherSrv = t.Result.Item5;
                if (t.Result.Item3 == ipBox.Text.Trim()) { answering = t.Result.Item2; checkedIp = t.Result.Item3; players = t.Result.Item4; }
                Draw();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Draw()
        {
            var lines = new List<StatusBox.Run[]>();
            string ip = ipBox.Text.Trim();
            var st = last;

            // mesmo modelo do Host: Radmin VPN / Rede / Servidor (+ IP no Radmin depois de conectar, + Jogadores)
            lines.Add(Radmin.StatusLine(st).ToArray());

            var nl = new List<StatusBox.Run> { new StatusBox.Run(L.T("Rede: ", "Network: "), Theme.Muted) };
            if (invite == null || invite.Net == "") nl.Add(new StatusBox.Run(L.T("(sem convite ainda)", "(no invite yet)"), Theme.Muted));
            else if (st == null || !st.LogFound) nl.Add(new StatusBox.Run(invite.Net, Theme.Text));
            else if (st.Networks.Contains(invite.Net)) { nl.Add(new StatusBox.Run(invite.Net, Theme.Text)); nl.Add(new StatusBox.Run("  ✓", Theme.Green)); }
            else { nl.Add(new StatusBox.Run(invite.Net, Theme.Text)); nl.Add(new StatusBox.Run(L.T("  ✗ fora da rede", "  ✗ not joined"), Theme.Gold)); }
            lines.Add(nl.ToArray());

            // o IP do proprio guest nunca aparece (so o do host): o dele so confunde
            bool validIp = Regex.IsMatch(ip, @"^26\.\d{1,3}\.\d{1,3}\.\d{1,3}$");

            var sv = new List<StatusBox.Run> { new StatusBox.Run(L.T("Servidor: ", "Server: "), Theme.Muted) };
            if (busy) sv.Add(new StatusBox.Run(L.T("conectando...", "connecting..."), Theme.Gold));
            else if (searching) sv.Add(new StatusBox.Run(L.T("procurando na rede do Radmin...", "searching the Radmin network..."), Theme.Gold));
            else if (!validIp && badInvite) sv.Add(new StatusBox.Run(L.T("✗ convite inválido", "✗ invalid invite"), Theme.BloodText, true));
            else if (!validIp) sv.Add(new StatusBox.Run(L.T("(sem convite ainda)", "(no invite yet)"), Theme.Muted));
            else if (IsOwn(ip)) sv.Add(new StatusBox.Run(L.T("✗ esse IP é o seu, não o do host", "✗ that IP is yours, not the host's"), Theme.BloodText, true));
            else if (checkedIp != ip) { sv.Add(new StatusBox.Run(ip, Theme.Text)); sv.Add(new StatusBox.Run("  ...", Theme.Muted)); }
            else if (answering) { sv.Add(new StatusBox.Run(ip, Theme.Text)); sv.Add(new StatusBox.Run(L.T("  ✓ ligado", "  ✓ on"), Theme.Green)); }
            else { sv.Add(new StatusBox.Run(ip, Theme.Text)); sv.Add(new StatusBox.Run(L.T("  ✗ sem resposta", "  ✗ not answering"), Theme.BloodText)); }
            lines.Add(sv.ToArray());

            // igual ao Host: quem esta conectado com voce no Radmin
            if (st != null && st.Peers.Count > 0 && st.PowerOn)
                lines.Add(new[] { new StatusBox.Run(L.T("Amigos no Radmin: ", "Friends in Radmin: "), Theme.Muted), new StatusBox.Run(string.Join(", ", st.Peers), Theme.Text) });
            // so quando for o caso: versao do mod diferente da do host
            if (ModMismatch())
                lines.Add(new[] { new StatusBox.Run("Mod: ", Theme.Muted), new StatusBox.Run(L.T("DIFERENTE do host", "DIFFERENT from host"), Theme.BloodText, true) });
            // jogadores por ultimo
            if (players != null)
                lines.Add(new[] { new StatusBox.Run(L.T("Jogadores: ", "Players: "), Theme.Muted),
                                  players.Count > 0 ? new StatusBox.Run(string.Join(", ", players), Theme.Text, true) : new StatusBox.Run(L.T("ninguém ainda", "nobody yet"), Theme.Muted) });

            status.Lines = lines;
            // caixa do tamanho exato das linhas (sem espaco sobrando); cresce quando a lista de nomes quebra
            int hh = D(10) * 2 + status.LineH * status.Rows;
            if (status.Height != hh) Relayout(hh - status.Height);
            status.Invalidate();

            back.SubText = Switch.PrevLabel();
            back.Enabled = !busy;
            go.Enabled = !busy;
            go.Dim = !HasServer();   // Conectar so com convite colado (ou IP escrito)
            back.Invalidate();
        }
        void Relayout(int dy)
        {
            SuspendLayout();
            status.Height += dy;
            foreach (Control c in Controls) if (c != status && c.Top > status.Top + 1 && c.Top >= ContentTop) c.Top += dy;
            FinishHeight(ClientSize.Height + dy);
            ResumeLayout();
            Invalidate(true);   // redesenha tudo (os links do rodape deixavam restos ao mudar de lugar)
        }

        // pasta do Seamless protegida (ex.: Arquivos de Programas): reabre como admin, so nesse caso
        bool NeedAdmin()
        {
            if (Switch.CanWrite() || Util.IsAdmin()) return false;
            if (!Msg.Ask(this, L.T("A pasta do Seamless Co-op é protegida pelo Windows.\nReabrir o RadminBell como administrador (o Windows pede 'Sim') e clicar de novo?",
                                   "The Seamless Co-op folder is protected by Windows.\nReopen RadminBell as administrator (Windows asks 'Yes') and click again?"),
                         L.T("Reabrir", "Reopen"), L.T("Cancelar", "Cancel"))) return true;
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--elevated") { Verb = "runas", UseShellExecute = true }).Dispose(); DarkForm.Exiting = true; Close(); }
            catch { Msg.Show(this, L.T("Precisa do 'Sim' no aviso do Windows.", "Windows needs a 'Yes'.")); }
            return true;
        }

        bool HasServer() { string ip = ipBox.Text.Trim(); return Regex.IsMatch(ip, @"^26\.\d{1,3}\.\d{1,3}\.\d{1,3}$") && !IsOwn(ip); }

        void DoConnect()
        {
            bool auto = autoPicked; autoPicked = false;
            if (busy) return;
            if (!HasServer())
            {
                Msg.Show(this, L.T("Cole primeiro o convite que o host mandou (botão 'Colar convite do amigo') ou escreva o IP do servidor (começa com 26.).",
                                   "First paste the invite the host sent ('Paste friend's invite' button) or type the server IP (starts with 26.)."));
                return;
            }
            if (Coop.Seamless && Switch.Root == null && !Switch.FindRoot(true)) return;
            if (NeedAdmin()) return;
            var st = Radmin.Read();
            if (!st.Installed)
            {
                Radmin.Open();
                Msg.Show(this, L.T("Radmin VPN não está instalado. O site dele foi aberto: instale, ligue e clique em Conectar de novo.",
                                   "Radmin VPN is not installed. Its website was opened: install it, turn it on and click Connect again."));
                return;
            }
            if (!st.PowerOn || !st.Ip.StartsWith("26."))
            {
                Radmin.Open();
                Msg.Show(this, L.T("O Radmin VPN está desligado: ligue ele (botão de energia) e clique em Conectar de novo.",
                                   "Radmin VPN is off: turn it on (power button) and click Connect again."));
                return;
            }
            // entrar na rede do host (passo a passo, detectado sozinho)
            if (invite != null && invite.Net != "" && st.LogFound && !st.Networks.Contains(invite.Net))
            {
                using (var g = new RadminGuide(false, invite.Net, invite.Pass)) { g.StartPosition = FormStartPosition.CenterParent; g.ShowDialog(this); if (!g.Done) return; }
            }
            string ip = ipBox.Text.Trim();
            if (ModMismatch() && !Msg.Ask(this, L.T("ATENÇÃO: vocês não estão com a mesma versão do mod.\n\nHost: " + ModInfo.Label(invite.Mod, invite.ModVer) + "\nVocê: " + ModInfo.Label(ModInfo.Hash(), ModInfo.Version()) + "\n\nCom versões diferentes vocês não se enxergam no jogo. Conectar mesmo assim?",
                                                     "WARNING: you are not on the same mod version.\n\nHost: " + ModInfo.Label(invite.Mod, invite.ModVer) + "\nYou: " + ModInfo.Label(ModInfo.Hash(), ModInfo.Version()) + "\n\nWith different versions you can't see each other in game. Connect anyway?"),
                                                L.T("Conectar mesmo assim", "Connect anyway"))) return;
            if (Util.GameOpen())
            {
                Msg.Show(this, L.T("Feche o jogo e o launcher da BBPort e clique em Conectar de novo.\n(Com o launcher aberto, ele desfaz a troca quando fecha.)",
                                   "Close the game and the BBPort launcher and click Connect again.\n(With the launcher open, it undoes the switch when it closes.)"));
                return;
            }
            busy = true; Cursor = Cursors.WaitCursor; Draw();
            Task.Run(() => { bool ok; string m; try { m = Switch.UseRadmin(ip, out ok); } catch (Exception ex) { ok = false; m = ex.Message; } return Tuple.Create(ok, m); })
                .ContinueWith(t =>
                {
                    busy = false; Cursor = Cursors.Default;
                    if (IsDisposed) return;
                    launcherSrv = Switch.LauncherServer();
                    Draw();
                    if (t.Result.Item1)
                    {
                        // conectou: guarda a rede (e a senha do convite, se veio) e o IP do host para as proximas vezes
                        if (invite != null && invite.Net != "") SavedNets.Save(invite.Net, invite.Pass, ip);
                        if (Msg.Ask(this, t.Result.Item2 + "\n\n" + L.T("Abrir o jogo agora?", "Start the game now?"), L.T("Abrir o jogo", "Start the game"), L.T("Depois", "Later")))
                            if (!Switch.OpenGame()) Msg.Show(this, Coop.Seamless
                                ? L.T("Abra o jogo pelo 'OPEN BBPORT LAUNCHER + BBSC.cmd' na pasta do Seamless Co-op.", "Start the game with 'OPEN BBPORT LAUNCHER + BBSC.cmd' in the Seamless Co-op folder.")
                                : L.T("Abra o jogo pelo launcher da BBPort (Bloodborne.exe).", "Start the game with the BBPort launcher (Bloodborne.exe)."));
                    }
                    else if (auto)
                    {
                        // a conexao automatica nao bateu: pede para colocar o convite ou o IP manualmente
                        Msg.Show(this, L.T("Não foi possível conectar automaticamente: o IP ou a senha não bateram.\nCole o convite do host ou digite o IP manualmente.",
                                           "Could not connect automatically: the IP or the password did not match.\nPaste the host's invite or type the IP manually.") + "\n\n" + t.Result.Item2);
                        ipBox.Text = ""; ipBox.Box.Focus();
                    }
                    else Msg.Show(this, t.Result.Item2);
                    Refresh_();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void DoReset()
        {
            if (busy) return;
            if (!Msg.Ask(this, L.T("Zerar o launcher da BBPort?\nO settings.json vira settings.json.radminbell e o launcher cria um novo (configurações e login do launcher voltam ao padrão).",
                                   "Reset the BBPort launcher?\nsettings.json is renamed to settings.json.radminbell and the launcher creates a new one (launcher settings and login go back to default)."),
                         L.T("Zerar", "Reset"), L.T("Cancelar", "Cancel"))) return;
            bool ok; string m;
            try { m = Switch.ResetLauncher(out ok); } catch (Exception ex) { ok = false; m = ex.Message; }
            launcherSrv = Switch.LauncherServer();
            Draw();
            Msg.Show(this, m);
        }

        void DoBack()
        {
            if (busy) return;
            if (NeedAdmin()) return;
            bool ok; string m;
            try { m = Switch.UseAnterior(out ok); } catch (Exception ex) { ok = false; m = ex.Message; }
            launcherSrv = Switch.LauncherServer();
            Draw();
            Msg.Show(this, m);
            Refresh_();
        }
    }
}
