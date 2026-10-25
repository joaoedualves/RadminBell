// 02.Switch Server.exe - troca o servidor do co-op para o RadminBell (Radmin VPN) e volta para o servidor de antes.
// No RadminBell ninguem digita login: quem entrou na rede do Radmin ja esta liberado, e a conta do jogo (que o
// shadNet exige) e criada sozinha, com o mesmo nome e uma senha aleatoria guardada so pro usuario do Windows.
// Na pasta tools do Seamless: account.dat = conta em uso; account-radmin.dat = conta do RadminBell;
// account-anterior.dat + launcher-anterior.json = servidor de antes (qualquer um), guardado na troca.
// Mesmos arquivos e formatos da versao .cmd (PowerShell), entao uma le o que a outra gravou.
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
using System.Reflection;

[assembly: AssemblyTitle("RadminBell - Switch Server")]
[assembly: AssemblyDescription("Points Seamless Co-op (BBSC) to a RadminBell server and back")]

namespace RadminBell
{
    static class Switch
    {
        public static string Root;   // pasta do Seamless Co-op (a que tem tools\play-bbport.ps1)
        // sem pasta do Seamless (apagada/nao achada) estes ficam null: o app abre e o Voltar ainda limpa o launcher
        static string InTools(string f) { return Root == null ? null : Path.Combine(Root, "tools", f); }
        public static string Active { get { return InTools("account.dat"); } }
        public static string RadminFile { get { return InTools("account-radmin.dat"); } }
        public static string PrevFile { get { return InTools("account-anterior.dat"); } }
        // fica no %LOCALAPPDATA%\RadminBell: sobrevive se a pessoa apagar a pasta do Seamless (o antigo ficava em tools\)
        static string PrevLauncher { get { return Path.Combine(Paths.Data, "launcher-anterior.json"); } }
        static string OldPrevLauncher { get { return InTools("launcher-anterior.json"); } }
        static readonly string LauncherFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"bbport-launcher\settings.json");
        static readonly string[] OnlineKeys = { "online", "online_pool", "online_server", "online_webapi", "online_community", "online_npid", "online_password" };

        // senha do account.dat: o play-bbport.ps1 do BBSC le com ConvertTo-SecureString, entao tem que ser
        // o mesmo formato do ConvertFrom-SecureString (DPAPI do usuario, texto UTF-16, hex). So o Switch usa.
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
            if (Root == null) return true;
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
                return false;   // ao abrir nao pergunta: so pede a pasta quando clicar em Usar o RadminBell
            }
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "RadminBell - " + L.T("escolha o 'OPEN BBPORT LAUNCHER + BBSC.cmd' do Seamless Co-op", "pick the Seamless Co-op 'OPEN BBPORT LAUNCHER + BBSC.cmd'");
                dlg.Filter = "Seamless Co-op (OPEN BBPORT LAUNCHER + BBSC.cmd)|OPEN BBPORT LAUNCHER*.cmd;PLAY BBPORT*.cmd";
                string sel = dlg.ShowDialog() == DialogResult.OK ? Path.GetDirectoryName(dlg.FileName) : "";
                if (sel == "" && ask) return false;
                if (!IsSeamless(sel))
                {
                    if (Msg.Ask(null, L.T("Essa não é a pasta do Seamless Co-op (falta tools\\play-bbport.ps1).\n\nO RadminBell só troca o servidor: precisa do Seamless Co-op (BBSC) da BBport já instalado e funcionando.\n\nAbrir a página de download do Seamless Co-op?",
                                          "This is not the Seamless Co-op folder (tools\\play-bbport.ps1 is missing).\n\nRadminBell only switches the server: you need Seamless Co-op (BBSC) for BBport already installed and working.\n\nOpen the Seamless Co-op download page?"),
                                  L.T("Abrir página", "Open page"), L.T("Fechar", "Close")))
                        Util.OpenUrl("https://github.com/CLSAMG22/BBSC/releases");
                    return false;
                }
                Root = sel; Paths.WriteText(Paths.SeamlessFile, Root); return true;
            }
        }

        public static Dictionary<string, object> Account(string f) { return Json.Read(f); }
        public static string Srv(Dictionary<string, object> a) { return Json.Str(a, "server") ?? ""; }
        public static bool IsRadmin(Dictionary<string, object> a) { return a != null && Srv(a).StartsWith("26."); }   // rede do Radmin VPN = 26.x
        public static string HostOf(Dictionary<string, object> a) { return Srv(a).Split(':')[0]; }

        // ---- pagina Online do launcher da BBport (tambem guarda servidor/conta: muda junto, senao ele pergunta ao abrir) ----
        static void WriteLauncher(Dictionary<string, object> cfg)
        {
            File.Copy(LauncherFile, LauncherFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
            Json.Write(LauncherFile, cfg);
        }
        static void SetLauncherOnline(Dictionary<string, object> acc)
        {
            var cfg = Json.Read(LauncherFile);
            if (cfg == null) return;
            var p = Srv(acc).Split(':'); string addr = p[0], port = p.Length > 1 ? p[1] : "31313";
            cfg["online"] = true; cfg["online_pool"] = "shadnet"; cfg["online_server"] = addr + ":" + port;
            cfg["online_webapi"] = "http://" + addr + ":31315"; cfg["online_community"] = "http://" + addr;
            cfg["online_npid"] = Json.Str(acc, "name"); cfg["online_password"] = Unprotect(Json.Str(acc, "password"));
            WriteLauncher(cfg);
        }

        // guarda o servidor atual (qualquer um que nao seja Radmin) antes de ir pro RadminBell
        static void SaveAnterior(Dictionary<string, object> cur)
        {
            if (IsRadmin(cur)) return;
            if (cur != null) File.Copy(Active, PrevFile, true);
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

        // desfaz tudo que o RadminBell mudou: volta o servidor guardado; sem ele, tira a conta e a pagina Online que a gente gravou
        public static string UseAnterior(out bool ok)
        {
            ok = true;
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

            if (prev != null)
            {
                string srv = Srv(prev);
                return L.T("Pronto: voltou para o servidor de antes (" + srv + ") e o que o RadminBell tinha mudado foi apagado.\nAbra o jogo normalmente, como antes.",
                           "Done: back on your previous server (" + srv + ") and RadminBell's changes were removed.\nStart the game as usual, like before.");
            }
            if (changed)
                return L.T("Pronto: tudo que o RadminBell tinha mudado foi apagado (conta e página Online do launcher).\nAbra o launcher e entre no seu servidor normalmente (se pedir, faça login de novo).",
                           "Done: everything RadminBell changed was removed (account and the launcher Online page).\nOpen the launcher and join your server as usual (log in again if it asks).");
            return L.T("Nada pra desfazer: o RadminBell não tinha mudado nada aqui.", "Nothing to undo: RadminBell had not changed anything here.");
        }

        // reset: tira o settings.json do launcher do caminho (vira settings.json.radminbell); a BBPort cria um novo sozinha
        public static string ResetLauncher(out bool ok)
        {
            ok = false;
            if (Util.Running("Bloodborne") || Util.Running("bb-probe"))
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

        // shadnet-sample.exe vem no Seamless Co-op (account\); senao o da pasta server ao lado do exe
        static string Tool()
        {
            string t = Path.Combine(Root, @"account\shadnet-sample.exe");
            return File.Exists(t) ? t : Path.Combine(Paths.Server, "shadnet-sample.exe");
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
            Paths.WriteText(Paths.HostFile, ip);
            var st = Radmin.Read();
            if (!st.PowerOn || !st.Ip.StartsWith("26."))
                return L.T("Radmin VPN desligado.\n\n1. Instale/abra o Radmin VPN (radmin-vpn.com) e ligue\n2. Entre na rede do servidor (nome e senha estão no convite)\n3. Clique aqui de novo.",
                           "Radmin VPN is off.\n\n1. Install/open Radmin VPN (radmin-vpn.com) and turn it on\n2. Join the server network (name and password are in the invite)\n3. Click here again.");
            if (!Util.HttpOk("http://" + ip + ":31315/status", 4000))
                return L.T("O servidor (" + ip + ") não respondeu.\nEle precisa estar ligado e você na rede dele no Radmin.", "The server (" + ip + ") did not answer.\nIt must be on and you must be in its Radmin network.");

            var cur = Account(Active);
            var acc = Account(RadminFile);
            if (acc == null || Srv(acc) != ip + ":31313")
            {
                string tool = Tool();
                if (!File.Exists(tool))
                    return L.T("Falta account\\shadnet-sample.exe na pasta do Seamless Co-op (vem no pacote do BBSC).", "account\\shadnet-sample.exe is missing in the Seamless Co-op folder (it comes with BBSC).");
                // conta automatica: mesmo nome da conta atual (ou do Windows) + senha aleatoria
                var prev = Account(PrevFile);
                string b = cur != null && !IsRadmin(cur) ? Json.Str(cur, "name") : prev != null ? Json.Str(prev, "name") : Environment.UserName;
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
                    var last = outp.Trim().Split('\n').Select(x => x.Trim()).Where(x => x != "").Reverse().Take(3).Reverse();
                    return L.T("O servidor não criou a conta:", "The server did not create the account:") + "\n" + string.Join("\n", last);
                }
                acc = new Dictionary<string, object> { { "server", ip + ":31313" }, { "name", name }, { "password", Protect(pw) } };
                Json.Write(RadminFile, acc);
            }
            SaveAnterior(cur);
            File.Copy(RadminFile, Active, true);
            SetLauncherOnline(acc);
            ok = true;
            string nm = Json.Str(acc, "name");
            return L.T("Pronto: RadminBell (" + ip + "), seu nome: " + nm + ".\nAbra o jogo pelo launcher do Seamless Co-op (\"OPEN BBPORT LAUNCHER + BBSC.cmd\" na pasta do BBSC).\nPra voltar: 02.Switch Server > Voltar ao meu servidor anterior.",
                       "Done: RadminBell (" + ip + "), your name: " + nm + ".\nStart the game with the Seamless Co-op launcher (\"OPEN BBPORT LAUNCHER + BBSC.cmd\" in the BBSC folder).\nTo go back: 02.Switch Server > Back to my previous server.");
        }
    }

    class SwitchForm : DarkForm
    {
        readonly StatusBox status = new StatusBox();
        readonly DarkInput ipBox = new DarkInput();
        readonly DarkButton go, back;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 4000 };
        bool busy, refreshing, answering;
        string checkedIp = "";
        RadminState last;

        public SwitchForm() : base(450, L.T("Trocar servidor", "Switch server"))
        {
            int pad = D(22), w = ClientSize.Width - pad * 2, y = ContentTop + D(18);

            status.Location = new Point(pad, y); status.Size = new Size(w, D(10) * 2 + status.LineH * 4);
            Controls.Add(status); y = status.Bottom + D(8);

            var note = new Label { Text = L.T("Todos do grupo precisam estar no MESMO servidor.", "Everyone in the group must be on the SAME server."),
                                   Font = Theme.Body(8.5f, FontStyle.Bold), ForeColor = Theme.BloodText, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleCenter,
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
            int cp = D(16), cw = w - cp * 2, cy = card.TitleH + D(10);
            string expl = L.T("Entre na rede do convite no Radmin VPN (Rede > Entrar numa rede existente) e coloque aqui o IP do servidor (começa com 26.). A conta do jogo é criada sozinha.",
                              "Join the invite's network in Radmin VPN (Network > Join an existing network) and type the server IP here (starts with 26.). The game account is created automatically.");
            var ef = Theme.Body(9);
            var es = TextRenderer.MeasureText(expl, ef, new Size(cw, 0), TextFormatFlags.WordBreak);
            card.Controls.Add(new Label { Text = expl, Font = ef, ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(cp, cy), Size = new Size(cw, es.Height + D(4)) });
            cy += es.Height + D(14);
            card.Controls.Add(new Label { Text = L.T("IP do servidor", "Server IP").ToUpperInvariant(), Font = Theme.Body(7.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(cp, cy), Size = new Size(cw, D(16)) });
            cy += D(18);
            int h = D(36), bw = D(40);
            ipBox.Location = new Point(cp, cy); ipBox.Size = new Size(cw - bw - D(8), h);
            var paste = new DarkButton { Glyph = "", Style = BtnStyle.Small, Font = Theme.Body(10), Location = new Point(cp + cw - bw, cy), Size = new Size(bw, h) };
            paste.Click += (s, e) =>
            {
                // cola o IP do convite (acha o 26.x.x.x no texto copiado)
                var m = Regex.Match(Clipboard.ContainsText() ? Clipboard.GetText() : "", @"\b26\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
                if (m.Success) ipBox.Text = m.Value;
                else Msg.Show(this, L.T("Nenhum IP 26.x no que foi copiado. Copie o convite (ou só o IP) e clique de novo.", "No 26.x IP in what you copied. Copy the invite (or just the IP) and click again."));
            };
            card.Controls.Add(ipBox); card.Controls.Add(paste); cy += h + D(14);
            go = new DarkButton { Text = L.T("Usar o RadminBell", "Use RadminBell"), Glyph = "", Style = BtnStyle.Primary, Location = new Point(cp, cy), Size = new Size(cw, D(44)) };
            go.Click += (s, e) => DoRadmin();
            card.Controls.Add(go); cy = go.Bottom + D(16);
            card.Size = new Size(w, cy); y = card.Bottom + D(18);

            Controls.Add(new Divider { Location = new Point(pad, y), Size = new Size(w, D(9)) }); y += D(9) + D(16);

            back = new DarkButton { Text = L.T("Voltar ao meu servidor anterior", "Back to my previous server"), Glyph = "", Location = new Point(pad, y), Size = new Size(w, D(52)) };
            back.Click += (s, e) => DoBack();
            Controls.Add(back); y = back.Bottom + D(8);

            var reset = new DarkButton { Text = L.T("Reset do launcher (zerar settings.json)", "Reset launcher (clear settings.json)"), Glyph = "", Style = BtnStyle.Small, Font = Theme.Body(8.5f), Location = new Point(pad + w / 4, y), Size = new Size(w / 2, D(28)) };
            reset.Click += (s, e) => DoReset();
            Controls.Add(reset); y = reset.Bottom + D(16);

            int lw = (w - D(12)) / 2;
            var folder = new LinkText { Text = L.T("Pasta do Seamless Co-op", "Seamless Co-op folder"), Location = new Point(pad, y), Size = new Size(lw, D(20)) };
            folder.Click += (s, e) => { if (Switch.FindRoot(true)) Draw(); };
            var lang = new LinkText { Text = "Language / Idioma", Location = new Point(pad + lw + D(12), y), Size = new Size(lw, D(20)) };
            lang.Click += (s, e) =>
            {
                if (L.Pick() == "") return;
                Process.Start(Application.ExecutablePath); Close();
            };
            Controls.Add(folder); Controls.Add(lang);
            FinishHeight(lang.Bottom + D(16));

            ipBox.Text = DefaultIp();
            ipBox.Box.TextChanged += (s, e) => { answering = false; checkedIp = ""; Draw(); };
            ipBox.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoRadmin(); } };
            timer.Tick += (s, e) => Refresh_();
            Load += (s, e) => { var a = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(a.Left + (a.Width - Width) / 2, a.Top + Math.Max(0, (a.Height - Height) / 2)); };
            Shown += (s, e) => { Draw(); Refresh_(); timer.Start(); ActiveControl = null; };
        }

        static string DefaultIp()
        {
            string ip = Paths.ReadLine1(Paths.HostFile);
            if (ip != "") return ip;
            if (Util.Running("shadnet")) { var r = Radmin.Read().Ip; if (r.StartsWith("26.")) return r; }   // servidor ligado neste PC: o IP e o proprio
            var cur = Switch.Account(Switch.Active);
            return Switch.IsRadmin(cur) ? Switch.HostOf(cur) : "";   // ja esta num RadminBell: o campo vem com o IP dele
        }

        void Refresh_()
        {
            if (refreshing || busy) return;
            refreshing = true;
            string ip = ipBox.Text.Trim();
            Task.Run(() =>
            {
                var st = Radmin.Read();
                bool ans = Regex.IsMatch(ip, @"^26\.\d{1,3}\.\d{1,3}\.\d{1,3}$") && st.PowerOn && Util.HttpOk("http://" + ip + ":31315/status", 2500);
                return Tuple.Create(st, ans, ip);
            }).ContinueWith(t =>
            {
                refreshing = false;
                if (t.IsFaulted || IsDisposed) return;
                last = t.Result.Item1;
                if (t.Result.Item3 == ipBox.Text.Trim()) { answering = t.Result.Item2; checkedIp = t.Result.Item3; }
                Draw();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Draw()
        {
            var lines = new List<StatusBox.Run[]>();
            var cur = Switch.Account(Switch.Active);
            var nowR = new List<StatusBox.Run> { new StatusBox.Run(L.T("Agora: ", "Now: "), Theme.Muted) };
            if (Switch.IsRadmin(cur)) { nowR.Add(new StatusBox.Run("RadminBell", Theme.Gold, true)); nowR.Add(new StatusBox.Run(" (" + Switch.HostOf(cur) + ")", Theme.Text)); }
            else if (cur != null) { nowR.Add(new StatusBox.Run(L.T("outro servidor", "other server"), Theme.Text, true)); nowR.Add(new StatusBox.Run(" (" + Switch.HostOf(cur) + ")", Theme.Muted)); }
            else nowR.Add(new StatusBox.Run(L.T("nenhum", "none"), Theme.Muted));
            lines.Add(nowR.ToArray());

            var st = last; var rv = new List<StatusBox.Run> { new StatusBox.Run("Radmin VPN: ", Theme.Muted) };
            if (st == null) rv.Add(new StatusBox.Run("...", Theme.Muted));
            else if (!st.Installed) rv.Add(new StatusBox.Run(L.T("NÃO INSTALADO", "NOT INSTALLED"), Theme.BloodText, true));
            else if (!st.PowerOn || !st.Ip.StartsWith("26.")) { rv.Add(new StatusBox.Run(L.T("DESLIGADO", "OFF"), Theme.BloodText, true)); rv.Add(new StatusBox.Run(L.T(" - ligue para jogar", " - turn it on to play"), Theme.BloodText)); }
            else if (st.LogFound && !st.ConnectedToServer) rv.Add(new StatusBox.Run(L.T("CONECTANDO...", "CONNECTING..."), Theme.Gold, true));
            else { rv.Add(new StatusBox.Run(L.T("LIGADO", "ON"), Theme.Green, true)); rv.Add(new StatusBox.Run("  " + st.Ip, Theme.Muted)); }
            lines.Add(rv.ToArray());

            string ip = ipBox.Text.Trim();
            var sv = new List<StatusBox.Run> { new StatusBox.Run(L.T("Servidor: ", "Server: "), Theme.Muted) };
            if (busy) sv.Add(new StatusBox.Run(L.T("trocando...", "switching..."), Theme.Gold));
            else if (!Regex.IsMatch(ip, @"^26\.\d{1,3}\.\d{1,3}\.\d{1,3}$")) sv.Add(new StatusBox.Run(L.T("(coloque o IP do convite)", "(type the invite IP)"), Theme.Muted));
            else if (checkedIp != ip) { sv.Add(new StatusBox.Run(ip, Theme.Text)); sv.Add(new StatusBox.Run("  ...", Theme.Muted)); }
            else if (answering) { sv.Add(new StatusBox.Run(ip, Theme.Text)); sv.Add(new StatusBox.Run(L.T("  ✓ respondendo", "  ✓ answering"), Theme.Green)); }
            else { sv.Add(new StatusBox.Run(ip, Theme.Text)); sv.Add(new StatusBox.Run(L.T("  ✗ sem resposta", "  ✗ not answering"), Theme.BloodText)); }
            lines.Add(sv.ToArray());

            if (Switch.Root != null) lines.Add(new[] { new StatusBox.Run("Seamless: ", Theme.Muted), new StatusBox.Run(Path.GetFileName(Switch.Root.TrimEnd('\\')), Theme.Text) });
            else lines.Add(new[] { new StatusBox.Run("Seamless: ", Theme.Muted), new StatusBox.Run(L.T("pasta não encontrada", "folder not found"), Theme.BloodText) });
            status.Lines = lines;
            status.Invalidate();

            var prev = Switch.Account(Switch.PrevFile);
            back.SubText = prev != null ? Switch.Srv(prev) : null;
            back.Enabled = !busy;
            go.Enabled = !busy;
            back.Invalidate();
        }

        // pasta do Seamless protegida (ex.: Arquivos de Programas): reabre como admin, so nesse caso
        bool NeedAdmin()
        {
            if (Switch.CanWrite() || Util.IsAdmin()) return false;
            if (!Msg.Ask(this, L.T("A pasta do Seamless Co-op é protegida pelo Windows.\nReabrir o Switch Server como administrador (o Windows pede 'Sim') e clicar de novo?",
                                   "The Seamless Co-op folder is protected by Windows.\nReopen Switch Server as administrator (Windows asks 'Yes') and click again?"),
                         L.T("Reabrir", "Reopen"), L.T("Cancelar", "Cancel"))) return true;
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--elevated") { Verb = "runas", UseShellExecute = true }).Dispose(); Close(); }
            catch { Msg.Show(this, L.T("Precisa do 'Sim' no aviso do Windows.", "Windows needs a 'Yes'.")); }
            return true;
        }

        void DoRadmin()
        {
            if (busy) return;
            if (Switch.Root == null && !Switch.FindRoot(true)) return;   // so aqui pede a pasta do Seamless
            if (NeedAdmin()) return;
            string ip = ipBox.Text;
            busy = true; Cursor = Cursors.WaitCursor; Draw();
            Task.Run(() => { bool ok; string m; try { m = Switch.UseRadmin(ip, out ok); } catch (Exception ex) { ok = false; m = ex.Message; } return Tuple.Create(ok, m); })
                .ContinueWith(t =>
                {
                    busy = false; Cursor = Cursors.Default;
                    if (IsDisposed) return;
                    Draw();
                    Msg.Show(this, t.Result.Item2);
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
            Draw();
            Msg.Show(this, m);
        }

        void DoBack()
        {
            if (busy) return;
            if (NeedAdmin()) return;
            bool ok; string m;
            try { m = Switch.UseAnterior(out ok); } catch (Exception ex) { ok = false; m = ex.Message; }
            Draw();
            Msg.Show(this, m);
            Refresh_();
        }
    }

    static class Program
    {
        static bool WaitMutex(Mutex m) { try { return m.WaitOne(10000); } catch (AbandonedMutexException) { return true; } }

        [STAThread]
        static int Main(string[] args)
        {
            Paths.Init();
            L.Load();
            using (var m = new Mutex(true, "RadminBell-Switch", out bool first))
            {
                // reaberto como admin: a janela antiga ainda pode estar fechando
                if (!first && !(args.Length > 0 && args[0] == "--elevated" && WaitMutex(m))) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (L.Lang == "" && L.Pick() == "") return 0;
                Switch.FindRoot(false);   // sem pasta tambem abre (Voltar funciona sem ela)
                Application.Run(new SwitchForm());
            }
            return 0;
        }
    }
}
