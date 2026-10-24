// 01.RadminBell.exe - painel do servidor de co-op na rede do Radmin VPN.
// Sem parametro = painel. --start / --stop = sem janela (inicio com o Windows). --setup (admin) = firewall.
// O servidor em si (shadnet + BBSC-GameServer) e o oficial do BBSC, de CLS_AMG, na pasta server ao lado do exe
// (vem junto no zip; o painel nao baixa nada).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("RadminBell - server panel")]
[assembly: AssemblyDescription("Starts/stops the official BBSC co-op server on a Radmin VPN network")]

namespace RadminBell
{
    static class Server
    {
        public const string Rule = "RadminBell";
        public static readonly string[] Programs = { "shadnet.exe", "BBSC-GameServer.exe" };
        public const string Download = "https://github.com/CLSAMG22/BBSC/releases";
        public static bool Present { get { return Programs.All(p => File.Exists(Path.Combine(Paths.Server, p))); } }

        // servidor oficial na pasta server ao lado do exe (vem no zip); se faltar, abre a pagina do BBSC
        public static string Install()
        {
            if (Present) return null;
            Util.OpenUrl(Download);
            return L.T("Falta a pasta server (servidor oficial do BBSC) ao lado do RadminBell. Abri a página: baixe o BBSC-0.0.2-Server.zip e coloque a pasta server dele aqui.",
                       "The server folder (official BBSC server) is missing next to RadminBell. I opened the page: download BBSC-0.0.2-Server.zip and put its server folder here.");
        }

        // regra por programa (TCP e UDP): so quem esta na rede Radmin (26.x) ou na sua casa entra;
        // tambem evita o aviso do firewall do Windows (Cancelar nele criaria uma regra de BLOQUEIO)
        static dynamic Policy() { return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")); }
        public static bool FirewallOk()
        {
            try
            {
                var need = new HashSet<string>();
                foreach (var p in Programs) { need.Add(p + " TCP"); need.Add(p + " UDP"); }
                foreach (dynamic r in Policy().Rules)
                {
                    string n = r.Name;
                    if (n == null || !n.StartsWith(Rule + " - ")) continue;
                    string key = n.Substring(Rule.Length + 3);
                    string exe = key.Substring(0, key.LastIndexOf(' '));
                    if ((int)r.Action == 1 && (int)r.Direction == 1 && string.Equals((string)r.ApplicationName, Path.Combine(Paths.Server, exe), StringComparison.OrdinalIgnoreCase))
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
            foreach (var p in Programs)
                foreach (var proto in new[] { 6, 17 })
                {
                    dynamic r = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                    r.Name = Rule + " - " + p + (proto == 6 ? " TCP" : " UDP");
                    r.ApplicationName = Path.Combine(Paths.Server, p);
                    r.Protocol = proto; r.Direction = 1; r.Action = 1; r.Profiles = 0x7FFFFFFF;
                    r.RemoteAddresses = "26.0.0.0/255.0.0.0,LocalSubnet"; r.Enabled = true;
                    pol.Rules.Add(r);
                }
        }

        public static void Stop()
        {
            foreach (var n in new[] { "shadnet", "BBSC-GameServer" })
                foreach (var p in Process.GetProcessesByName(n)) { try { p.Kill(); p.WaitForExit(3000); } catch { } p.Dispose(); }
        }
        // shadnet com saida direto no arquivo de log (handle herdado): continua rodando com o painel fechado
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct StartupInfo
        {
            public int cb; public string reserved, desktop, title;
            public int x, y, w, h, xChars, yChars, fill, flags; public short show, reserved2; public IntPtr reserved3, stdIn, stdOut, stdErr;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcInfo { public IntPtr process, thread; public int pid, tid; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string dir, ref StartupInfo si, out ProcInfo pi);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr h, int mask, int flags);

        static void StartShadnet(string log)
        {
            Environment.SetEnvironmentVariable("SHADNET_BLOODBORNE_SEAMLESS_COOP", "1");
            Environment.SetEnvironmentVariable("SHADNET_BLOODBORNE_RE_TRACE", "1");
            string exe = Path.Combine(Paths.Server, "shadnet.exe");
            using (var fs = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                IntPtr h = fs.SafeFileHandle.DangerousGetHandle();
                SetHandleInformation(h, 1, 1);   // HANDLE_FLAG_INHERIT
                var si = new StartupInfo { flags = 0x100, stdOut = h, stdErr = h };   // STARTF_USESTDHANDLES
                si.cb = Marshal.SizeOf(si);
                ProcInfo pi;
                if (!CreateProcess(exe, new StringBuilder("\"" + exe + "\""), IntPtr.Zero, IntPtr.Zero, true, 0x08000000, IntPtr.Zero, Paths.Server, ref si, out pi))   // CREATE_NO_WINDOW
                    throw new Win32Exception();
                CloseHandle(pi.thread); CloseHandle(pi.process);
            }
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
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { StartShadnet(Path.Combine(Paths.Logs, "shadnet-" + stamp + ".log")); }
            catch (Exception e) { return L.T("Não consegui abrir o shadnet.exe: ", "Could not start shadnet.exe: ") + e.Message; }
            Thread.Sleep(2000);
            Process.Start(new ProcessStartInfo(Path.Combine(Paths.Server, "BBSC-GameServer.exe"),
                "--shadnet " + ip + " --host 0.0.0.0 --advertise " + ip + " --summon-interval 5 --no-playlog --log-dir \"" + Paths.Logs + "\"")
            { WorkingDirectory = Paths.Server, UseShellExecute = false, CreateNoWindow = true }).Dispose();
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(1000);
                if (Answering(ip)) { Paths.WriteText(Paths.HostFile, ip); return null; }   // quem liga tambem joga: o Switch Server ja vem com este IP
            }
            return L.T("Os programas abriram mas o servidor não respondeu - veja os logs (botão 'Abrir logs').",
                       "The programs started but the server did not answer - check the logs ('Open logs' button).");
        }

        // ligar sozinho ao entrar no Windows
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool AutoStart
        {
            get { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(Rule) != null; }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (value) k.SetValue(Rule, "\"" + Application.ExecutablePath + "\" --start"); else k.DeleteValue(Rule, false);
            }
        }
    }

    class Panel_ : DarkForm
    {
        readonly StatusBox status = new StatusBox();
        readonly DarkInput name = new DarkInput(), pass = new DarkInput();
        readonly DarkButton start, stop;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 4000 };
        bool busy, refreshing;
        string busyText;
        RadminState last;

        public Panel_() : base(450)
        {
            int pad = D(22), w = ClientSize.Width - pad * 2, y = ContentTop + D(18);

            status.Location = new Point(pad, y); status.Size = new Size(w, D(10) * 2 + status.LineH * 4);
            Controls.Add(status); y = status.Bottom + D(14);

            var openRadmin = new DarkButton { Text = L.T("Abrir Radmin VPN", "Open Radmin VPN"), Glyph = "", Location = new Point(pad, y), Size = new Size(w, D(44)) };
            openRadmin.Click += (s, e) =>
            {
                if (!Radmin.Open()) Msg.Show(this, L.T("Radmin VPN não está instalado. Abri o site: instale e clique aqui de novo.",
                                                     "Radmin VPN is not installed. I opened the website: install it and click here again."));
            };
            Controls.Add(openRadmin); y = openRadmin.Bottom + D(22);

            // ---- cartao da rede ----
            var card = new Card { Title = L.T("Rede do Radmin VPN", "Radmin VPN Network"), TitleH = D(20), Location = new Point(pad, y) };
            Controls.Add(card);
            int cp = D(16), cw = w - cp * 2, cy = card.TitleH + D(10);
            string expl = L.T("No Radmin VPN: Rede > Criar rede, com EXATAMENTE este nome e senha (maiúsculas contam). Os amigos entram nela pelo convite.",
                              "In Radmin VPN: Network > Create network, with EXACTLY this name and password (case matters). Friends join it with the invite.");
            var ef = Theme.Body(9);
            var es = TextRenderer.MeasureText(expl, ef, new Size(cw, 0), TextFormatFlags.WordBreak);
            card.Controls.Add(new Label { Text = expl, Font = ef, ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(cp, cy), Size = new Size(cw, es.Height + D(4)) });
            cy += es.Height + D(14);
            cy = Field(card, L.T("Nome", "Name"), name, cp, cy, cw);
            cy = Field(card, L.T("Senha", "Password"), pass, cp, cy, cw);
            var gen = new DarkButton { Text = L.T("Gerar nome e senha aleatórios", "Generate random name and password"), Glyph = "", Style = BtnStyle.Small,
                                       Font = Theme.Body(9), Location = new Point(cp, cy + D(4)), Size = new Size(cw, D(36)) };
            gen.Click += (s, e) => Generate();
            card.Controls.Add(gen); cy = gen.Bottom + D(16);
            card.Size = new Size(w, cy); y = card.Bottom + D(18);

            Controls.Add(new Divider { Location = new Point(pad, y), Size = new Size(w, D(9)) }); y += D(9) + D(16);

            var invite = new DarkButton { Text = L.T("Copiar convite", "Copy invite"), Glyph = "", Style = BtnStyle.Gold, Location = new Point(pad, y), Size = new Size(w, D(44)) };
            invite.Click += (s, e) => CopyInvite();
            Controls.Add(invite); y = invite.Bottom + D(6);
            var hint = new Label { Text = L.T("Mande para os amigos (Discord/WhatsApp): rede, senha e IP do servidor.", "Send it to your friends (Discord/WhatsApp): network, password and server IP."),
                                   Font = Theme.Body(8), ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(pad, y), Size = new Size(w, D(18)) };
            Controls.Add(hint); y = hint.Bottom + D(16);

            int bw = (w - D(12)) / 2;
            start = new DarkButton { Text = L.T("Ligar", "Start"), Glyph = "", Style = BtnStyle.Start, Location = new Point(pad, y), Size = new Size(bw, D(48)) };
            stop = new DarkButton { Text = L.T("Desligar", "Stop"), Glyph = "", Style = BtnStyle.Stop, Location = new Point(pad + bw + D(12), y), Size = new Size(bw, D(48)) };
            start.Click += (s, e) => DoStart();
            stop.Click += (s, e) => DoStop();
            Controls.Add(start); Controls.Add(stop); y = start.Bottom + D(10);

            var logs = new DarkButton { Text = L.T("Abrir logs", "Open logs"), Glyph = "", Style = BtnStyle.Outline, Font = Theme.Body(9), Location = new Point(pad, y), Size = new Size(w, D(36)) };
            logs.Click += (s, e) => { Directory.CreateDirectory(Paths.Logs); Process.Start("explorer.exe", "\"" + Paths.Logs + "\""); };
            Controls.Add(logs); y = logs.Bottom + D(12);

            var auto = new DarkCheck { Text = L.T("Ligar sozinho quando eu entrar no Windows", "Start by itself when I log into Windows"), Location = new Point(pad, y), Size = new Size(w, D(22)) };
            try { auto.Checked = Server.AutoStart; } catch { }
            auto.CheckedChanged += (s, e) => { try { Server.AutoStart = auto.Checked; } catch { } };
            Controls.Add(auto); y = auto.Bottom + D(14);

            var lang = new LinkText { Text = "Language / Idioma", Location = new Point(pad, y), Size = new Size(w, D(20)) };
            lang.Click += (s, e) =>
            {
                if (L.Pick() == "") return;
                Process.Start(Application.ExecutablePath); Close();
            };
            Controls.Add(lang);
            FinishHeight(lang.Bottom + D(16));

            LoadNet();
            name.Box.Leave += (s, e) => SaveNet(); pass.Box.Leave += (s, e) => SaveNet();
            name.Box.TextChanged += (s, e) => Draw();
            FormClosing += (s, e) => SaveNet();
            timer.Tick += (s, e) => Refresh_();
            Load += (s, e) => { var a = Screen.FromPoint(Cursor.Position).WorkingArea; Location = new Point(a.Left + (a.Width - Width) / 2, a.Top + Math.Max(0, (a.Height - Height) / 2)); };
            Shown += (s, e) => { Refresh_(); timer.Start(); ActiveControl = null; };
        }

        int Field(Control parent, string label, DarkInput input, int x, int y, int w)
        {
            parent.Controls.Add(new Label { Text = label.ToUpperInvariant(), Font = Theme.Body(7.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Theme.Panel, Location = new Point(x, y), Size = new Size(w, D(16)) });
            y += D(18);
            int h = D(36), bw = D(40);
            input.Location = new Point(x, y); input.Size = new Size(w - bw - D(8), h);
            var copy = new DarkButton { Glyph = "", Style = BtnStyle.Small, Font = Theme.Body(10), Location = new Point(x + w - bw, y), Size = new Size(bw, h) };
            copy.Click += (s, e) =>
            {
                var t = input.Text.Trim();
                if (t == "") { NeedNet(); return; }
                SaveNet(); Clipboard.SetText(t);
            };
            parent.Controls.Add(input); parent.Controls.Add(copy);
            return y + h + D(12);
        }

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
            SaveNet(); Draw();
        }

        void CopyInvite()
        {
            if (!NeedNet()) return;
            SaveNet();
            string ip = last != null && last.Ip.StartsWith("26.") ? last.Ip : null, n = name.Text.Trim(), p = pass.Text.Trim(), t;
            if (L.Lang == "en")
                t = "Bloodborne co-op - RadminBell server\r\n" +
                    "1. Install Radmin VPN: https://www.radmin-vpn.com\r\n" +
                    "2. In Radmin: Network > Join an existing network\r\n" +
                    "   Name: " + n + "\r\n" +
                    "   Password: " + p + "\r\n" +
                    "3. In the RadminBell folder: open '02.Switch Server.exe', type the server IP and click RadminBell (account is created by itself)\r\n" +
                    "   Server IP: " + (ip ?? "(open Radmin VPN and copy the invite again)") + "\r\n" +
                    "4. Start the game with the Seamless Co-op launcher (OPEN BBPORT LAUNCHER + BBSC.cmd). To go back to your old server: Switch Server > Back to my previous server.";
            else
                t = "Co-op Bloodborne - servidor RadminBell\r\n" +
                    "1. Instale o Radmin VPN: https://www.radmin-vpn.com\r\n" +
                    "2. No Radmin: Rede > Entrar numa rede existente\r\n" +
                    "   Nome: " + n + "\r\n" +
                    "   Senha: " + p + "\r\n" +
                    "3. Na pasta RadminBell: abra '02.Switch Server.exe', coloque o IP do servidor e clique em RadminBell (conta criada sozinha)\r\n" +
                    "   IP do servidor: " + (ip ?? "(abra o Radmin VPN e copie o convite de novo)") + "\r\n" +
                    "4. Abra o jogo pelo launcher do Seamless Co-op (OPEN BBPORT LAUNCHER + BBSC.cmd). Pra voltar ao servidor de antes: Switch Server > Voltar ao meu servidor anterior.";
            Clipboard.SetText(t);
            Msg.Show(this, L.T("Convite copiado. Cole no Discord/WhatsApp:", "Invite copied. Paste it on Discord/WhatsApp:") + "\n\n" + t);
        }

        // ---- ligar / desligar ----
        void DoStart()
        {
            if (busy) return;
            if (!NeedNet()) return;
            SaveNet();
            var st = Radmin.Read();
            if (!st.Installed)
            {
                Radmin.Open();
                Msg.Show(this, L.T("Radmin VPN não está instalado. Abri o site: instale, crie a rede e clique em Ligar de novo.",
                                   "Radmin VPN is not installed. I opened the website: install it, create the network and click Start again."));
                return;
            }
            if (!st.PowerOn || !st.Ip.StartsWith("26."))
            {
                Radmin.Open();
                Msg.Show(this, L.T("O Radmin VPN está desligado: ligue ele (botão de energia) e clique em Ligar de novo.",
                                   "Radmin VPN is off: turn it on (power button) and click Start again."));
                return;
            }
            string n = name.Text.Trim();
            if (st.LogFound && !st.Networks.Contains(n) &&
                !Msg.Ask(this, L.T("Não achei a rede '" + n + "' no Radmin VPN. Crie ela (Rede > Criar rede) com EXATAMENTE este nome e senha, senão os amigos não entram.\n\nLigar mesmo assim?",
                                   "I could not find the network '" + n + "' in Radmin VPN. Create it (Network > Create network) with EXACTLY this name and password, or your friends can't join.\n\nStart anyway?"),
                         L.T("Ligar mesmo assim", "Start anyway"))) return;
            busy = true; busyText = Server.Present ? L.T("Ligando...", "Starting...") : L.T("Baixando o servidor oficial do BBSC (1ª vez, ~45 MB)...", "Downloading the official BBSC server (first time, ~45 MB)...");
            start.Enabled = stop.Enabled = false; Cursor = Cursors.WaitCursor; Draw();
            string ip = st.Ip;
            Task.Run(() => Server.Start(ip)).ContinueWith(t =>
            {
                busy = false; start.Enabled = stop.Enabled = true; Cursor = Cursors.Default;
                Refresh_();
                var err = t.IsFaulted ? t.Exception.GetBaseException().Message : t.Result;
                if (err != null) Msg.Show(this, err);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        void DoStop()
        {
            if (busy) return;
            busy = true; busyText = L.T("Desligando...", "Stopping..."); Draw();
            Task.Run(() => Server.Stop()).ContinueWith(t => { busy = false; Refresh_(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ---- status (le Radmin + processos em segundo plano) ----
        bool shad, game, answering;
        void Refresh_()
        {
            if (refreshing) return;
            refreshing = true;
            Task.Run(() =>
            {
                var st = Radmin.Read();
                bool s = Util.Running("shadnet"), g = Util.Running("BBSC-GameServer");
                bool a = s && g && Server.Answering(st.Ip);
                return Tuple.Create(st, s, g, a);
            }).ContinueWith(t =>
            {
                refreshing = false;
                if (t.IsFaulted || IsDisposed) return;
                last = t.Result.Item1; shad = t.Result.Item2; game = t.Result.Item3; answering = t.Result.Item4;
                Draw();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void Draw()
        {
            var L1 = new List<StatusBox.Run>(); var L2 = new List<StatusBox.Run>(); var L3 = new List<StatusBox.Run>(); var L4 = new List<StatusBox.Run>();
            var st = last;
            string n = name.Text.Trim();
            L1.Add(new StatusBox.Run("Radmin VPN: ", Theme.Muted));
            bool netOk = false;
            if (st == null) L1.Add(new StatusBox.Run("...", Theme.Muted));
            else if (!st.Installed) { L1.Add(new StatusBox.Run(L.T("NÃO INSTALADO", "NOT INSTALLED"), Theme.BloodText, true)); L1.Add(new StatusBox.Run(L.T(" - amigos não entram", " - friends can't connect"), Theme.BloodText)); }
            else if (!st.PowerOn || !st.Ip.StartsWith("26.")) { L1.Add(new StatusBox.Run(L.T("DESLIGADO", "OFF"), Theme.BloodText, true)); L1.Add(new StatusBox.Run(L.T(" - amigos não entram", " - friends can't connect"), Theme.BloodText)); }
            else if (st.LogFound && !st.ConnectedToServer) L1.Add(new StatusBox.Run(L.T("CONECTANDO...", "CONNECTING..."), Theme.Gold, true));
            else
            {
                netOk = !st.LogFound || (n != "" && st.Networks.Contains(n));
                if (netOk) L1.Add(new StatusBox.Run(L.T("ONLINE", "ONLINE"), Theme.Green, true));
                else L1.Add(new StatusBox.Run(L.T("SEM A REDE", "NO NETWORK"), Theme.Gold, true));
            }

            L2.Add(new StatusBox.Run(L.T("Rede: ", "Network: "), Theme.Muted));
            if (n == "") L2.Add(new StatusBox.Run(L.T("(sem nome ainda)", "(no name yet)"), Theme.Muted));
            else if (st == null || !st.LogFound) L2.Add(new StatusBox.Run(n, Theme.Text));
            else if (st.Networks.Contains(n)) { L2.Add(new StatusBox.Run(n, Theme.Text)); L2.Add(new StatusBox.Run("  ✓", Theme.Green)); }
            else { L2.Add(new StatusBox.Run(n, Theme.Text)); L2.Add(new StatusBox.Run(L.T("  ✗ não criada", "  ✗ not created"), Theme.Gold)); }

            L3.Add(new StatusBox.Run(L.T("IP no Radmin: ", "Radmin IP: "), Theme.Muted));
            if (st != null && st.Ip.StartsWith("26.")) { L3.Add(new StatusBox.Run(st.Ip, Theme.Text, true)); L3.Add(new StatusBox.Run(L.T("  (fixo)", "  (fixed)"), Theme.Muted)); }
            else L3.Add(new StatusBox.Run("-", Theme.Muted));

            if (busy) L4.Add(new StatusBox.Run(busyText, Theme.Gold));
            else
            {
                string run = L.T("rodando", "running"), stp = L.T("parado", "stopped");
                L4.Add(new StatusBox.Run("shadnet: ", Theme.Muted)); L4.Add(new StatusBox.Run(shad ? run : stp, shad ? Theme.Green : Theme.BloodText));
                L4.Add(new StatusBox.Run(L.T("   jogo: ", "   game: "), Theme.Muted)); L4.Add(new StatusBox.Run(game ? run : stp, game ? Theme.Green : Theme.BloodText));
                if (shad && game && !answering) L4.Add(new StatusBox.Run(L.T("  (sem resposta)", "  (not answering)"), Theme.Gold));
            }
            status.Lines = new List<StatusBox.Run[]> { L1.ToArray(), L2.ToArray(), L3.ToArray(), L4.ToArray() };
            if (st != null && st.Peers.Count > 0 && st.PowerOn)
                status.Lines.Add(new[] { new StatusBox.Run(L.T("Amigos no Radmin: ", "Friends in Radmin: "), Theme.Muted), new StatusBox.Run(string.Join(", ", st.Peers), Theme.Text) });
            int h = D(10) * 2 + status.LineH * status.Lines.Count;
            if (status.Height != h) Relayout(h - status.Height);
            status.Invalidate();
        }
        void Relayout(int dy)
        {
            SuspendLayout();
            status.Height += dy;
            foreach (Control c in Controls) if (c != status && c.Top > status.Top + 1 && c.Top >= ContentTop) c.Top += dy;
            FinishHeight(ClientSize.Height + dy);
            ResumeLayout();
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Paths.Init();
            L.Load();
            string a = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (a == "--setup") { try { Server.SetupFirewall(); return 0; } catch { return 1; } }
            if (a == "--stop") { Server.Stop(); return 0; }
            if (a == "--start")
            {
                // inicio com o Windows: espera o Radmin conectar
                Thread.Sleep(30000);
                string ip = "";
                for (int i = 0; i < 60 && !(ip = Radmin.Read().Ip).StartsWith("26."); i++) Thread.Sleep(5000);
                return Server.Start(ip) == null ? 0 : 1;
            }
            using (var m = new Mutex(true, "RadminBell-Panel", out bool first))
            {
                if (!first) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (L.Lang == "" && L.Pick() == "") return 0;
                Application.Run(new Panel_());
            }
            return 0;
        }
    }
}
