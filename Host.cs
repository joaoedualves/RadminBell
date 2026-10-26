// Tela do Host: rede do Radmin VPN (criada pelo passo a passo), convite, ligar/desligar o servidor e jogadores conectados.
// O servidor em si (shadnet + BBSC-GameServer) e o oficial do BBSC, de CLS_AMG, na pasta server ao lado do exe
// (vem junto no zip; o RadminBell nao baixa nada).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RadminBell
{
    static class Server
    {
        public const string Rule = "RadminBell";
        public static readonly string[] Programs = { "shadnet.exe", "BBSC-GameServer.exe" };
        // arquivos do servidor oficial 0.0.2 que vem no zip do RadminBell (pasta server ao lado do exe)
        static readonly string[] Needed = { "BBSC-GameServer.exe", "shadnet.exe", "icuuc.dll", "Qt6Core.dll", "Qt6HttpServer.dll", "Qt6Network.dll",
            "Qt6Sql.dll", "Qt6WebSockets.dll", "scoreboards.cfg", "shadnet.cfg", "worlds.cfg", @"networkinformation\qnetworklistmanager.dll",
            @"sqldrivers\qsqlibase.dll", @"sqldrivers\qsqlite.dll", @"sqldrivers\qsqlmimer.dll", @"sqldrivers\qsqloci.dll", @"sqldrivers\qsqlodbc.dll",
            @"sqldrivers\qsqlpsql.dll", @"tls\qcertonlybackend.dll", @"tls\qschannelbackend.dll" };
        public static List<string> Missing() { return Needed.Where(f => !File.Exists(Path.Combine(Paths.Server, f))).ToList(); }
        public static bool Present { get { return Missing().Count == 0; } }

        // faltou arquivo do servidor: o caminho e baixar o RadminBell de novo (o zip completo)
        public static string Install()
        {
            var miss = Missing();
            if (miss.Count == 0) return null;
            string list = string.Join(", ", miss.Take(4)) + (miss.Count > 4 ? L.T(" e mais " + (miss.Count - 4), " and " + (miss.Count - 4) + " more") : "");
            return L.T("Faltam arquivos do servidor na pasta 'server' ao lado do RadminBell:\n" + list + "\n\nBaixe o RadminBell de novo (o zip completo) e extraia por cima desta pasta. As suas configurações não se perdem.",
                       "Server files are missing in the 'server' folder next to RadminBell:\n" + list + "\n\nDownload RadminBell again (the full zip) and extract it over this folder. Your settings are kept.");
        }

        // regras por programa (TCP e UDP): so quem esta na rede Radmin (26.x) ou na sua casa entra;
        // tambem evita o aviso do firewall do Windows (Cancelar nele criaria uma regra de BLOQUEIO).
        // + a lista de jogadores do RadminBell (TCP 31316), so leitura, so pela rede do Radmin.
        static dynamic Policy() { return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")); }
        static IEnumerable<KeyValuePair<string, string>> Wanted()
        {
            foreach (var p in Programs) { yield return new KeyValuePair<string, string>(p + " TCP", Path.Combine(Paths.Server, p)); yield return new KeyValuePair<string, string>(p + " UDP", Path.Combine(Paths.Server, p)); }
            yield return new KeyValuePair<string, string>("RadminBell.exe TCP", Application.ExecutablePath);   // lista de jogadores
            yield return new KeyValuePair<string, string>("RadminBell.exe UDP", Application.ExecutablePath);   // resposta ao "quem e host?"
        }
        public static bool FirewallOk()
        {
            try
            {
                var need = Wanted().ToDictionary(x => x.Key, x => x.Value);
                foreach (dynamic r in Policy().Rules)
                {
                    string n = r.Name;
                    if (n == null || !n.StartsWith(Rule + " - ")) continue;
                    string key = n.Substring(Rule.Length + 3), app;
                    if (need.TryGetValue(key, out app) && (int)r.Action == 1 && (int)r.Direction == 1 && string.Equals((string)r.ApplicationName, app, StringComparison.OrdinalIgnoreCase))
                        need.Remove(key);
                }
                return need.Count == 0;
            }
            catch { return false; }
        }
        public static void SetupFirewall()
        {
            dynamic pol = Policy();
            var del = new List<string>();
            foreach (dynamic r in pol.Rules)
            {
                string n = r.Name ?? "", app = r.ApplicationName ?? "";
                bool ours = n.StartsWith(Rule + " - ") || n.StartsWith("BRHunt-Radmin - ") || n.StartsWith("BBSC Servidor Radmin - ");
                bool block = (int)r.Action == 0 && Programs.Any(p => string.Equals(app, Path.Combine(Paths.Server, p), StringComparison.OrdinalIgnoreCase));
                if (ours || block) del.Add(n);
            }
            foreach (var n in del.Distinct()) for (int i = 0; i < 20; i++) { try { pol.Rules.Remove(n); } catch { break; } }
            foreach (var w in Wanted())
            {
                bool app = w.Key.StartsWith("RadminBell.exe");
                dynamic r = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                r.Name = Rule + " - " + w.Key;
                r.ApplicationName = w.Value;
                r.Protocol = w.Key.EndsWith("TCP") ? 6 : 17; r.Direction = 1; r.Action = 1; r.Profiles = 0x7FFFFFFF;
                if (app) r.LocalPorts = Players.Port.ToString();
                r.RemoteAddresses = app ? "26.0.0.0/255.0.0.0" : "26.0.0.0/255.0.0.0,LocalSubnet"; r.Enabled = true;
                pol.Rules.Add(r);
            }
        }

        public static bool Running { get { return Util.Running("shadnet") && Util.Running("BBSC-GameServer"); } }
        // alguma parte do servidor DESTA pasta rodando (para nao deixar nada para tras ao fechar)
        public static bool AnyRunning { get { return Ours().Count > 0; } }
        // processos do servidor que sao da pasta server deste RadminBell (outro RadminBell em outra pasta nao e tocado)
        static List<Process> Ours()
        {
            var res = new List<Process>();
            foreach (var n in new[] { "shadnet", "BBSC-GameServer" })
                foreach (var p in Process.GetProcessesByName(n))
                {
                    string f = null;
                    try { f = p.MainModule.FileName; } catch { }
                    if (f == null || f.StartsWith(Paths.Server + "\\", StringComparison.OrdinalIgnoreCase)) res.Add(p); else p.Dispose();
                }
            return res;
        }
        public static void Stop()
        {
            foreach (var p in Ours()) { try { p.Kill(); p.WaitForExit(3000); } catch { } p.Dispose(); }
        }
        // shadnet sem janela; continua rodando com o RadminBell fechado (sem log proprio: o shadnet nao tem opcao de log)
        static void StartShadnet()
        {
            var psi = new ProcessStartInfo(Path.Combine(Paths.Server, "shadnet.exe")) { WorkingDirectory = Paths.Server, UseShellExecute = false, CreateNoWindow = true };
            psi.EnvironmentVariables["SHADNET_BLOODBORNE_SEAMLESS_COOP"] = "1";
            psi.EnvironmentVariables["SHADNET_BLOODBORNE_RE_TRACE"] = "1";
            Process.Start(psi).Dispose();
        }

        public static bool Answering(string ip) { return !string.IsNullOrEmpty(ip) && Util.HttpOk("http://" + ip + ":31315/status", 2000); }

        public static string Start(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                return L.T("Radmin VPN sem IP 26.x. Abra o Radmin VPN, ligue e crie a rede.", "Radmin VPN has no 26.x IP. Open Radmin VPN, turn it on and create the network.");
            var err = Install(); if (err != null) return err;
            if (!FirewallOk())
            {
                try
                {
                    var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--setup") { Verb = "runas", UseShellExecute = true });
                    p.WaitForExit();
                }
                catch { return L.T("Precisa do 'Sim' no aviso do Windows (firewall) na 1ª vez.", "Windows needs a 'Yes' (firewall) the first time."); }
            }
            Stop();
            Directory.CreateDirectory(Paths.Logs);
            try { StartShadnet(); }
            catch (Exception e) { return L.T("Não foi possível abrir o shadnet.exe: ", "Could not start shadnet.exe: ") + e.Message; }
            Thread.Sleep(2000);
            Process.Start(new ProcessStartInfo(Path.Combine(Paths.Server, "BBSC-GameServer.exe"),
                "--shadnet " + ip + " --host 0.0.0.0 --advertise " + ip + " --summon-interval 5 --no-playlog --log-dir \"" + Paths.Logs + "\"")
            { WorkingDirectory = Paths.Server, UseShellExecute = false, CreateNoWindow = true }).Dispose();
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(1000);
                if (Answering(ip)) { Paths.WriteText(Paths.HostFile, ip); return null; }
            }
            return L.T("Os programas abriram mas o servidor não respondeu - veja os logs (botão 'Abrir logs').",
                       "The programs started but the server did not answer - check the logs ('Open logs' button).");
        }
    }

    class HostForm : DarkForm
    {
        readonly StatusBox status = new StatusBox();
        readonly DarkInput name = new DarkInput(), pass = new DarkInput();
        readonly DarkButton start, stop, create;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1500 };   // rapido: a rede criada aparece logo
        bool busy, refreshing;
        string busyText;
        RadminState last;
        List<string> players = new List<string>();

        public HostForm() : base(450, "Host")
        {
            int pad = D(22), w = ClientSize.Width - pad * 2, y = ContentTop + D(14);

            var role = new RoleTitle("host") { Location = new Point(pad, y), Size = new Size(w, D(30)) };
            Controls.Add(role); y = role.Bottom + D(12);

            status.Location = new Point(pad, y); status.Size = new Size(w, D(10) * 2 + status.LineH * 6);
            Controls.Add(status); y = status.Bottom + D(14);

            var openRadmin = new DarkButton { Text = L.T("Abrir Radmin VPN", "Open Radmin VPN"), Glyph = "\uE71B", Location = new Point(pad, y), Size = new Size(w, D(44)) };
            openRadmin.Click += (s, e) =>
            {
                if (!Radmin.Open()) Msg.Show(this, L.T("Radmin VPN não está instalado. O site dele foi aberto: instale e clique aqui de novo.",
                                                     "Radmin VPN is not installed. Its website was opened: install it and click here again."));
            };
            Controls.Add(openRadmin); y = openRadmin.Bottom + D(22);

            // ---- cartao da rede ----
            var card = new Card { Title = L.T("Rede do Radmin", "Radmin network"), TitleH = D(20), Location = new Point(pad, y) };
            Controls.Add(card);
            int cp = D(16), cw = w - cp * 2, cy = card.TitleH + D(12);
            string ph = L.T("escreva ou clique abaixo para gerar nome e senha aleatórios", "type it or click below to generate a random name and password");
            name.Placeholder = ph; pass.Placeholder = ph;
            cy = Field(card, L.T("Nome", "Name"), name, cp, cy, cw);
            cy = Field(card, L.T("Senha", "Password"), pass, cp, cy, cw);
            var gen = new DarkButton { Text = L.T("Gerar nome e senha aleatórios", "Generate random name and password"), Glyph = "\uE72C", Style = BtnStyle.Small,
                                       Font = Theme.Body(9), Location = new Point(cp, cy + D(2)), Size = new Size(cw, D(34)) };
            gen.Click += (s, e) => Generate();
            card.Controls.Add(gen); cy = gen.Bottom + D(10);
            create = new DarkButton { Text = L.T("Criar a rede no Radmin", "Create the network in Radmin"), Glyph = "\uE710", Style = BtnStyle.Primary, Location = new Point(cp, cy), Size = new Size(cw, D(42)) };
            create.Click += (s, e) => CreateNet();
            card.Controls.Add(create); cy = create.Bottom + D(16);
            card.Size = new Size(w, cy); y = card.Bottom + D(18);

            Controls.Add(new Divider { Location = new Point(pad, y), Size = new Size(w, D(9)) }); y += D(9) + D(16);

            var invite = new DarkButton { Text = L.T("Copiar convite", "Copy invite"), Glyph = "\uE724", Style = BtnStyle.Gold, Location = new Point(pad, y), Size = new Size(w, D(44)) };
            invite.Click += (s, e) => CopyInvite();
            Controls.Add(invite); y = invite.Bottom + D(8);
            int hw = (w - D(12)) / 2;
            var cIp = new DarkButton { Text = L.T("Copiar só o IP", "Copy IP only"), Style = BtnStyle.Small, Font = Theme.Body(8.5f), Location = new Point(pad, y), Size = new Size(hw, D(30)) };
            var cPw = new DarkButton { Text = L.T("Copiar só a senha", "Copy password only"), Style = BtnStyle.Small, Font = Theme.Body(8.5f), Location = new Point(pad + hw + D(12), y), Size = new Size(hw, D(30)) };
            cIp.Click += (s, e) => { var ip = OwnIp(); if (ip == null) Msg.Show(this, L.T("O Radmin VPN está sem IP 26.x: ligue o Radmin.", "Radmin VPN has no 26.x IP: turn Radmin on.")); else Clipboard.SetText(ip); };
            cPw.Click += (s, e) => { if (NeedNet()) Clipboard.SetText(pass.Text.Trim()); };
            Controls.Add(cIp); Controls.Add(cPw); y = cIp.Bottom + D(16);

            int bw = (w - D(12)) / 2;
            start = new DarkButton { Text = L.T("Ligar", "Start"), Glyph = "\uE768", Style = BtnStyle.Start, Location = new Point(pad, y), Size = new Size(bw, D(48)) };
            stop = new DarkButton { Text = L.T("Desligar", "Stop"), Glyph = "\uE71A", Style = BtnStyle.Stop, Location = new Point(pad + bw + D(12), y), Size = new Size(bw, D(48)) };
            start.Click += (s, e) => DoStart();
            stop.Click += (s, e) => DoStop();
            Controls.Add(start); Controls.Add(stop); y = start.Bottom + D(10);

            var logs = new DarkButton { Text = L.T("Abrir logs", "Open logs"), Glyph = "\uE8B7", Style = BtnStyle.Outline, Font = Theme.Body(9), Location = new Point(pad, y), Size = new Size(w, D(36)) };
            logs.Click += (s, e) => { Directory.CreateDirectory(Paths.Logs); Process.Start("explorer.exe", "\"" + Paths.Logs + "\""); };
            Controls.Add(logs); y = logs.Bottom + D(14);

            y += Footer.Add(this, pad, w, y, true);
            FinishHeight(y + D(10));

            LoadNet();
            name.Box.Leave += (s, e) => SaveNet(); pass.Box.Leave += (s, e) => SaveNet();
            name.Box.TextChanged += (s, e) => Draw(); pass.Box.TextChanged += (s, e) => Draw();
            FormClosing += (s, e) => SaveNet();
            timer.Tick += (s, e) => Refresh_();
            Load += (s, e) => { var a = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(a.Left + (a.Width - Width) / 2, a.Top + Math.Max(0, (a.Height - Height) / 2)); };
            // a janela de redes so abre depois que o app terminou de aparecer (senao ela nasce atras dele)
            Shown += (s, e) => { Players.Serve(); Refresh_(); timer.Start(); ActiveControl = null; if (name.Text.Trim() == "") BeginInvoke((Action)(() => { Activate(); PickNet(); })); };
        }

        int Field(Control parent, string label, DarkInput input, int x, int y, int w)
        {
            parent.Controls.Add(new Label { Text = label.ToUpperInvariant(), Font = Theme.Body(7.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(x, y), Size = new Size(w, D(16)) });
            y += D(18);
            int h = D(36), bw = D(40);
            input.Location = new Point(x, y); input.Size = new Size(w - bw - D(8), h);
            var copy = new DarkButton { Glyph = "\uE8C8", Style = BtnStyle.Small, Font = Theme.Body(10), Location = new Point(x + w - bw, y), Size = new Size(bw, h) };
            copy.Click += (s, e) =>
            {
                var t = input.Text.Trim();
                if (t == "") { NeedNet(); return; }
                SaveNet(); Clipboard.SetText(t);
            };
            parent.Controls.Add(input); parent.Controls.Add(copy);
            return y + h + D(10);
        }

        string OwnIp() { return last != null && last.Ip.StartsWith("26.") ? last.Ip : null; }

        // ---- nome/senha da rede ----
        void LoadNet()
        {
            try
            {
                if (!File.Exists(Paths.NetFile)) return;
                var l = File.ReadAllLines(Paths.NetFile);
                if (l.Length > 0) name.Text = l[0].Trim();
                if (l.Length > 1) pass.Text = l[1].Trim();
            }
            catch { }
        }
        void SaveNet()
        {
            string n = name.Text.Trim(), p = pass.Text.Trim();
            if (n == "" && p == "") return;
            try { Paths.WriteText(Paths.NetFile, n + "\r\n" + p + "\r\n"); } catch { }
        }
        bool Filled { get { return name.Text.Trim() != "" && pass.Text.Trim() != ""; } }
        // a rede escrita no app ja existe no Radmin deste PC (com o mesmo nome)?
        bool NetInRadmin(RadminState st) { return st != null && st.Installed && st.PowerOn && st.Ip.StartsWith("26.") && (!st.LogFound || st.Networks.Contains(name.Text.Trim())); }
        bool NeedName()
        {
            if (name.Text.Trim() != "") return true;
            Msg.Show(this, L.T("Escreva o nome da rede (ou clique em 'Gerar nome e senha aleatórios').", "Type the network name (or click 'Generate random name and password')."));
            return false;
        }
        // redes feitas pelo RadminBell que ainda estao no Radmin deste PC: a pessoa escolhe uma e ja vem nome + senha.
        // Rede feita fora do app nao aparece (a senha dela nao e conhecida).
        void PickNet()
        {
            var st = Radmin.Read();
            if (!st.LogFound || st.Networks.Count == 0) return;
            var nets = SavedNets.Names().Where(x => st.Networks.Contains(x)).OrderBy(x => x).ToList();
            if (nets.Count == 0) return;
            int i = NetPicker.Ask(this, nets, L.T("Criar nova", "Create new"));
            if (i < 0) return;
            name.Text = nets[i];
            pass.Text = SavedNets.Pass(nets[i]) ?? "";
            SaveNet(); Draw();
        }
        bool NeedNet()
        {
            if (name.Text.Trim() != "" && pass.Text.Trim() != "") return true;
            Msg.Show(this, L.T("Escreva o nome e a senha da rede (ou clique em 'Gerar nome e senha aleatórios').",
                               "Type the network name and password (or click 'Generate random name and password')."));
            return false;
        }
        static readonly string[] Places = { "Yharnam", "Hemwick", "Cainhurst", "Byrgenwerth", "Oedon", "Loran", "Pthumeru", "Isz", "Hypogean",
            "Lumenwood", "Kos", "Amygdala", "Ludwig", "Gehrman", "Maria", "Djura", "Eileen", "Mensis", "Paleblood", "BloodMoon", "Insight",
            "Chalice", "Vileblood", "Choir", "Healing", "OldYharnam" };
        static readonly string[] Groups = { "Hunters", "Cacadores", "Vigil", "Lamp", "Bell", "Workshop", "Nightmare", "Dream", "Church", "Hunt" };
        void Generate()
        {
            if (name.Text.Trim() != "" && !Msg.Ask(this, L.T("Trocar o nome e a senha atuais? Se a rede já foi criada no Radmin, os amigos vão precisar do convite novo.",
                    "Replace the current name and password? If the network was already created in Radmin, your friends will need the new invite."),
                    L.T("Trocar", "Replace"))) return;
            var b = new byte[3]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            name.Text = Places[b[0] % Places.Length] + "-" + Groups[b[1] % Groups.Length] + "-" + (100 + (b[2] * 4) % 899);
            pass.Text = Util.RandomFrom("abcdefghjkmnpqrstuvwxyz23456789", 10);   // sem 0/o/1/l/i (confundem ao digitar)
            SavedNets.Save(name.Text.Trim(), pass.Text.Trim());   // gerada pelo app: a senha fica guardada
            SaveNet(); Draw();
        }

        // passo a passo: copia o nome, abre o Radmin e espera a rede aparecer no service.log
        void CreateNet()
        {
            if (!NeedNet()) return;
            SaveNet();
            var st = Radmin.Read();
            if (st.Networks.Contains(name.Text.Trim()))
            {
                Msg.Show(this, L.T("A rede '" + name.Text.Trim() + "' já está no seu Radmin VPN. ✓", "The network '" + name.Text.Trim() + "' is already in your Radmin VPN. ✓"));
                return;
            }
            using (var g = new RadminGuide(true, name.Text.Trim(), pass.Text.Trim())) { g.StartPosition = FormStartPosition.CenterParent; g.ShowDialog(this); }
            Refresh_();
        }

        void CopyInvite()
        {
            if (!NeedName()) return;
            SaveNet();
            string ip = OwnIp();
            if (ip == null) { Msg.Show(this, L.T("O Radmin VPN está sem IP 26.x: ligue o Radmin e copie o convite de novo.", "Radmin VPN has no 26.x IP: turn Radmin on and copy the invite again.")); return; }
            var inv = new Invite { Ip = ip, Net = name.Text.Trim(), Pass = pass.Text.Trim(), Mod = ModInfo.Hash(), ModVer = ModInfo.Version() };
            Clipboard.SetText(inv.Encode());
            Msg.Show(this, L.T("Convite copiado. Mande para os amigos (Discord/WhatsApp).\nEles abrem o RadminBell como Guest e clicam em 'Colar convite'.",
                               "Invite copied. Send it to your friends (Discord/WhatsApp).\nThey open RadminBell as Guest and click 'Paste invite'."));
        }

        // ---- ligar / desligar ----
        void DoStart()
        {
            if (busy) return;
            if (!NeedName()) return;
            SaveNet();
            var st = Radmin.Read();
            if (!st.Installed)
            {
                Radmin.Open();
                Msg.Show(this, L.T("Radmin VPN não está instalado. O site dele foi aberto: instale, crie a rede e clique em Ligar de novo.",
                                   "Radmin VPN is not installed. Its website was opened: install it, create the network and click Start again."));
                return;
            }
            if (!st.PowerOn || !st.Ip.StartsWith("26."))
            {
                Radmin.Open();
                Msg.Show(this, L.T("O Radmin VPN está desligado: ligue ele (botão de energia) e clique em Ligar de novo.",
                                   "Radmin VPN is off: turn it on (power button) and click Start again."));
                return;
            }
            // a rede do app e a do Radmin precisam ser a mesma (senao os amigos entram numa rede e o servidor fica em outra)
            string n = name.Text.Trim();
            if (!NetInRadmin(st))
            {
                if (Msg.Ask(this, L.T("Não dá para ligar: a rede '" + n + "' não está no seu Radmin VPN.\n\nA rede escrita aqui e a do Radmin precisam ser IGUAIS (mesmo nome). Crie a rede no Radmin com este nome e senha, ou escreva aqui o nome exato da rede que você criou.",
                                      "Can't start: the network '" + n + "' is not in your Radmin VPN.\n\nThe network typed here and the one in Radmin must be the SAME (same name). Create the network in Radmin with this name and password, or type here the exact name of the network you created."),
                            L.T("Criar a rede no Radmin", "Create the network in Radmin"), L.T("Fechar", "Close")))
                    CreateNet();
                return;
            }
            var miss = Server.Install();
            if (miss != null) { Msg.Show(this, miss); return; }
            // o host tambem joga: o jogo dele passa a apontar para o proprio servidor (com o launcher fechado)
            // o host tambem joga: sem a pasta do Seamless, pede ela antes (igual ao Conectar do Guest)
            if (Coop.Seamless && Switch.Root == null) Switch.FindRoot(true);
            bool pointGame = Coop.Seamless ? Switch.Root != null : true;
            if (pointGame && Util.GameOpen())
                Msg.Show(this, L.T("O servidor vai ligar, mas o seu jogo/launcher está aberto: feche e clique em Ligar de novo para o SEU jogo entrar no servidor.",
                                   "The server will start, but your game/launcher is open: close it and click Start again so YOUR game joins the server."));
            busy = true; busyText = L.T("Ligando...", "Starting...");
            start.Enabled = stop.Enabled = false; Cursor = Cursors.WaitCursor; Draw();
            string ip = st.Ip;
            Task.Run(() =>
            {
                string err = Server.Start(ip);
                if (err != null) return err;
                if (!pointGame || Util.GameOpen()) return null;
                bool ok; string m;
                try { m = Switch.UseRadmin(ip, out ok); } catch (Exception ex) { ok = false; m = ex.Message; }
                return ok ? "OK|" + m : L.T("O servidor ligou, mas o seu jogo não foi apontado para ele:\n", "The server started, but your game was not pointed to it:\n") + m;
            }).ContinueWith(t =>
            {
                busy = false; start.Enabled = stop.Enabled = true; Cursor = Cursors.Default;
                Refresh_();
                var r = t.IsFaulted ? t.Exception.GetBaseException().Message : t.Result;
                if (r == null) Msg.Show(this, L.T("Servidor ligado. Copie o convite e mande para os amigos.", "Server started. Copy the invite and send it to your friends."));
                else if (r.StartsWith("OK|"))
                {
                    // a mesma mensagem do Guest ao conectar: "Pronto: seu jogo vai entrar no servidor..." + Abrir o jogo agora?
                    if (Msg.Ask(this, r.Substring(3) + "\n\n" + L.T("Abrir o jogo agora?", "Start the game now?"),
                                L.T("Abrir o jogo", "Start the game"), L.T("Depois", "Later")))
                        if (!Switch.OpenGame()) Msg.Show(this, Coop.Seamless
                            ? L.T("Abra o jogo pelo 'OPEN BBPORT LAUNCHER + BBSC.cmd' na pasta do Seamless Co-op.", "Start the game with 'OPEN BBPORT LAUNCHER + BBSC.cmd' in the Seamless Co-op folder.")
                            : L.T("Abra o jogo pelo launcher da BBPort (Bloodborne.exe).", "Start the game with the BBPort launcher (Bloodborne.exe)."));
                }
                else Msg.Show(this, r);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        void DoStop()
        {
            if (busy) return;
            if (!shad && !game) { Msg.Show(this, L.T("O servidor já está desligado.", "The server is already off.")); return; }
            busy = true; busyText = L.T("Desligando...", "Stopping..."); Draw();
            Task.Run(() => Server.Stop()).ContinueWith(t => { busy = false; Refresh_(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---- status (le Radmin + processos + log do servidor em segundo plano) ----
        bool shad, game, answering;
        void Refresh_()
        {
            if (refreshing || !Visible) return;
            refreshing = true;
            Task.Run(() =>
            {
                var st = Radmin.Read();
                bool s = Util.Running("shadnet"), g = Util.Running("BBSC-GameServer");
                bool a = s && g && Server.Answering(st.Ip);
                var pl = s && g ? Players.FromLog() : new List<string>();
                return Tuple.Create(st, s, g, a, pl);
            }).ContinueWith(t =>
            {
                refreshing = false;
                if (t.IsFaulted || IsDisposed) return;
                last = t.Result.Item1; shad = t.Result.Item2; game = t.Result.Item3; answering = t.Result.Item4; players = t.Result.Item5;
                Draw();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Draw()
        {
            var L1 = new List<StatusBox.Run>(); var L2 = new List<StatusBox.Run>(); var L3 = new List<StatusBox.Run>(); var L4 = new List<StatusBox.Run>();
            var st = last;
            string n = name.Text.Trim();
            L1.AddRange(Radmin.StatusLine(st));

            L2.Add(new StatusBox.Run(L.T("Rede: ", "Network: "), Theme.Muted));
            if (n == "") L2.Add(new StatusBox.Run(L.T("(sem nome ainda)", "(no name yet)"), Theme.Muted));
            else if (st == null || !st.LogFound) L2.Add(new StatusBox.Run(n, Theme.Text));
            else if (st.Networks.Contains(n)) { L2.Add(new StatusBox.Run(n, Theme.Text)); L2.Add(new StatusBox.Run("  ✓", Theme.Green)); }
            else { L2.Add(new StatusBox.Run(n, Theme.Text)); L2.Add(new StatusBox.Run(L.T("  ✗ não criada", "  ✗ not created"), Theme.Gold)); }

            L3.Add(new StatusBox.Run(L.T("IP no Radmin: ", "Radmin IP: "), Theme.Muted));
            if (st != null && st.Ip.StartsWith("26.")) L3.Add(new StatusBox.Run(st.Ip, Theme.Text, true));
            else L3.Add(new StatusBox.Run("-", Theme.Muted));

            if (busy) L4.Add(new StatusBox.Run(busyText, Theme.Gold));
            else
            {
                L4.Add(new StatusBox.Run(L.T("Servidor: ", "Server: "), Theme.Muted));
                if (shad && game && answering) L4.Add(new StatusBox.Run(L.T("LIGADO", "ON"), Theme.Green, true));
                else if (shad && game) L4.Add(new StatusBox.Run(L.T("ligando / sem resposta", "starting / not answering"), Theme.Gold));
                else if (shad || game) L4.Add(new StatusBox.Run(L.T("incompleto - clique em Ligar", "incomplete - click Start"), Theme.Gold));
                else if (!Server.Present) L4.Add(new StatusBox.Run(L.T("arquivos faltando - baixe o RadminBell de novo", "files missing - download RadminBell again"), Theme.BloodText, true));
                else L4.Add(new StatusBox.Run(L.T("desligado", "off"), Theme.BloodText));
            }
            status.Lines = new List<StatusBox.Run[]> { L1.ToArray(), L2.ToArray(), L3.ToArray(), L4.ToArray() };
            if (shad && game)
                status.Lines.Add(new[] { new StatusBox.Run(L.T("Jogadores: ", "Players: "), Theme.Muted),
                                         players.Count > 0 ? new StatusBox.Run(string.Join(", ", players), Theme.Text, true) : new StatusBox.Run(L.T("ninguém ainda", "nobody yet"), Theme.Muted) });
            if (st != null && st.Peers.Count > 0 && st.PowerOn)
                status.Lines.Add(new[] { new StatusBox.Run(L.T("Amigos no Radmin: ", "Friends in Radmin: "), Theme.Muted), new StatusBox.Run(string.Join(", ", st.Peers), Theme.Text) });
            int h = D(10) * 2 + status.LineH * Math.Max(6, status.Lines.Count);
            if (status.Height != h) Relayout(h - status.Height);
            status.Invalidate();

            // Criar a rede: so com nome e senha. Ligar: so com a rede do app criada no Radmin (mesmo nome).
            bool created = Filled && st != null && st.LogFound && st.Networks.Contains(n);
            string ct = created ? L.T("Rede criada", "Network created") : L.T("Criar a rede no Radmin", "Create the network in Radmin");
            if (create.Text != ct)
            {
                create.Text = ct;
                create.Glyph = created ? "" : "";
                create.Style = created ? BtnStyle.Start : BtnStyle.Primary;
                create.Invalidate();
            }
            create.Dim = !Filled;
            start.Dim = !(name.Text.Trim() != "" && NetInRadmin(st));   // rede que ja existe: a senha nao e preciso para ligar
            Players.NetName = name.Text.Trim();
            stop.Dim = !(shad || game);   // Desligar so com o servidor (ou parte dele) rodando
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
    }
}
