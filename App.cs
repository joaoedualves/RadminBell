// RadminBell.exe - app unico: na 1a vez pergunta o idioma e o papel (Host ou Guest) e abre a tela de cada um.
// Host = liga o servidor oficial do BBSC na rede do Radmin VPN. Guest = aponta o jogo para o servidor de um amigo.
// O X esconde o app na area de notificacao (perto do relogio); fechar de verdade pelo menu do icone.
// Parametros: --setup (admin) = regras do firewall; --stop = desliga o servidor sem janela.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("RadminBell")]
[assembly: AssemblyDescription("Private Bloodborne Seamless Co-op server over Radmin VPN: host or join")]

namespace RadminBell
{
    // ---------- qual co-op o jogador usa ----------
    static class Coop
    {
        public static bool Seamless = true;   // false = Bloodborne normal (so a pagina Online do launcher da BBPort)
        public static void Load()
        {
            string c = Paths.ReadLine1(Paths.CoopFile);
            Seamless = c != "normal";
        }
        public static void Set(bool seamless) { Seamless = seamless; Paths.WriteText(Paths.CoopFile, seamless ? "seamless" : "normal"); }
        public static string Name { get { return Seamless ? "Seamless Co-op" : L.T("Bloodborne normal", "Regular Bloodborne"); } }
    }

    // ---------- versao do mod BBSC (pelo conteudo do bbsc.dll, nao pelo tamanho) ----------
    static class ModInfo
    {
        static string cacheFile, cacheHash; static DateTime cacheTime;
        public static string Hash()
        {
            if (!Coop.Seamless) return "normal";
            if (Switch.Root == null) return "";
            string f = Path.Combine(Switch.Root, @"bbsc\bbsc.dll");
            try
            {
                var fi = new FileInfo(f);
                if (!fi.Exists) return "";
                if (cacheFile == f && cacheTime == fi.LastWriteTimeUtc) return cacheHash;
                using (var sha = SHA256.Create()) using (var s = File.OpenRead(f))
                    cacheHash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").Substring(0, 10);
                cacheFile = f; cacheTime = fi.LastWriteTimeUtc;
                return cacheHash;
            }
            catch { return ""; }
        }
        public static string Version()
        {
            if (!Coop.Seamless || Switch.Root == null) return "";
            var j = Json.Read(Path.Combine(Switch.Root, @"bbsc\mod.json"));
            return Json.Str(j, "version") ?? "";
        }
        public static string Label(string hash, string ver)
        {
            if (hash == "normal") return L.T("Bloodborne normal", "Regular Bloodborne");
            if (string.IsNullOrEmpty(hash)) return "?";
            return "BBSC " + (string.IsNullOrEmpty(ver) ? "" : ver + " ") + "(" + hash.Substring(0, Math.Min(6, hash.Length)) + ")";
        }
    }

    // ---------- codigo de convite: RB1-... (IP, rede, senha, co-op e versao do mod do host) ----------
    class Invite
    {
        public string Ip = "", Net = "", Pass = "", Mod = "", ModVer = "";
        public string Encode()
        {
            var raw = Encoding.UTF8.GetBytes(string.Join("\n", "1", Ip, Net, Pass, Mod, ModVer));
            return "RB1-" + Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        // aceita o codigo RB1-... ou qualquer texto com um IP 26.x
        public static Invite Parse(string text)
        {
            text = text ?? "";
            var m = Regex.Match(text, @"RB1-([A-Za-z0-9_\-]+)");
            if (m.Success)
            {
                try
                {
                    string b = m.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                    b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
                    var p = Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('\n');
                    if (p.Length >= 4 && p[0] == "1")
                        return new Invite { Ip = p[1], Net = p[2], Pass = p[3], Mod = p.Length > 4 ? p[4] : "", ModVer = p.Length > 5 ? p[5] : "" };
                }
                catch { }
            }
            var ip = Regex.Match(text, @"\b26\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");
            return ip.Success ? new Invite { Ip = ip.Value } : null;
        }
        public static Invite Saved() { try { return File.Exists(Paths.InviteFile) ? Parse(File.ReadAllText(Paths.InviteFile)) : null; } catch { return null; } }
        public void Save() { Paths.WriteText(Paths.InviteFile, Encode()); }
    }

    // ---------- jogadores no servidor: lidos do log do servidor (host) e repassados ao guest pela rede do Radmin ----------
    static class Players
    {
        public const int Port = 31316;
        static readonly Regex Req = new Regex(@"^\[\d\d:\d\d:\d\d\.\d+\] (\d+\.\d+\.\d+\.\d+) (GET|POST) ");
        static readonly Regex Ident = new Regex(@"identity: (\S+) -> user_id");
        static readonly Regex RelayReg = new Regex(@"shadPS4 relay: (\S+) registered from (\d+\.\d+\.\d+\.\d+):");
        static readonly Regex RelayOn = new Regex(@"relay: (\d+\.\d+\.\d+\.\d+):(\d+) connected");
        static readonly Regex RelayOff = new Regex(@"relay: (\d+\.\d+\.\d+\.\d+):(\d+) left");

        public static List<string> FromLog(string dir = null)
        {
            var res = new List<string>();
            dir = dir ?? Paths.Logs;
            try
            {
                if (!Directory.Exists(dir)) return res;
                var f = new DirectoryInfo(dir).GetFiles("gameserver-*.log").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                if (f == null) return res;
                string text;
                using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) text = sr.ReadToEnd();
                var names = new Dictionary<string, string>(); var active = new Dictionary<string, string>();
                string lastIp = null;
                foreach (var raw in text.Split('\n'))
                {
                    var s = raw.Trim(); Match m;
                    if ((m = Req.Match(s)).Success) lastIp = m.Groups[1].Value;
                    else if ((m = Ident.Match(s)).Success && lastIp != null) names[lastIp] = m.Groups[1].Value;
                    else if ((m = RelayReg.Match(s)).Success) { if (!names.ContainsKey(m.Groups[2].Value)) names[m.Groups[2].Value] = m.Groups[1].Value; }
                    else if ((m = RelayOn.Match(s)).Success) active[m.Groups[1].Value + ":" + m.Groups[2].Value] = m.Groups[1].Value;
                    else if ((m = RelayOff.Match(s)).Success) active.Remove(m.Groups[1].Value + ":" + m.Groups[2].Value);
                }
                foreach (var ip in active.Values.Distinct())
                {
                    string n; res.Add(names.TryGetValue(ip, out n) ? n : ip);
                }
            }
            catch { }
            return res.Distinct().ToList();
        }

        // rede do Host (o HostForm atualiza): vai na resposta do "quem e host?"
        public static string NetName = "";
        // UDP: responde ao "quem e host?" do Guest com o IP no Radmin e o nome da rede
        static UdpClient disc;
        static void ServeDiscovery()
        {
            if (disc != null) return;
            try { disc = new UdpClient(new IPEndPoint(IPAddress.Any, Port)); }
            catch { disc = null; return; }
            new Thread(() =>
            {
                while (true)
                {
                    IPEndPoint from = null; byte[] b;
                    try { b = disc.Receive(ref from); } catch (SocketException) { continue; } catch { return; }
                    if (Encoding.ASCII.GetString(b) != "RBDISC1") continue;
                    try
                    {
                        var st = Radmin.Read();
                        string ip = st.Ip;
                        if (!ip.StartsWith("26.") || !Server.Running) continue;   // so responde com o servidor ligado
                        // nome da rede: o do campo; vazio -> o salvo no RadminBell; senao a unica rede criada neste PC
                        string net = NetName;
                        if (net == "") net = Paths.ReadLine1(Paths.NetFile);
                        if (net == "" && st.Owned.Count == 1) net = st.Owned.First();
                        var r = Encoding.UTF8.GetBytes("RBHOST1\n" + ip + "\n" + net);
                        disc.Send(r, r.Length, from);
                    }
                    catch { }
                }
            }) { IsBackground = true }.Start();
        }

        // so leitura: GET /players -> {"players":[...]} (a regra do firewall libera so a rede do Radmin)
        static TcpListener listener;
        public static void Serve()
        {
            ServeDiscovery();
            if (listener != null) return;
            try { listener = new TcpListener(IPAddress.Any, Port); listener.Start(); }
            catch { listener = null; return; }
            var t = new Thread(() =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = listener.AcceptTcpClient(); } catch { return; }
                    ThreadPool.QueueUserWorkItem(_ => Answer(c));
                }
            }) { IsBackground = true };
            t.Start();
        }
        static void Answer(TcpClient c)
        {
            try
            {
                using (c)
                {
                    c.ReceiveTimeout = 2000; c.SendTimeout = 2000;
                    var s = c.GetStream(); var buf = new byte[1024];
                    int n = s.Read(buf, 0, buf.Length);
                    string req = Encoding.ASCII.GetString(buf, 0, Math.Max(0, n));
                    string body = req.StartsWith("GET /players") ? new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "players", FromLog() } }) : "{}";
                    var b = Encoding.UTF8.GetBytes(body);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.0 " + (body == "{}" ? "404 Not Found" : "200 OK") + "\r\nContent-Type: application/json\r\nContent-Length: " + b.Length + "\r\nConnection: close\r\n\r\n");
                    s.Write(head, 0, head.Length); s.Write(b, 0, b.Length);
                }
            }
            catch { }
        }
        // guest: pergunta ao app do host; null = sem resposta (app do host fechado)
        public static List<string> Fetch(string ip)
        {
            var t = Util.HttpGet("http://" + ip + ":" + Port + "/players", 1500);
            if (t == null) return null;
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(t);
                object v; return d.TryGetValue("players", out v) && v is System.Collections.ArrayList ? ((System.Collections.ArrayList)v).Cast<object>().Select(x => x.ToString()).ToList() : new List<string>();
            }
            catch { return null; }
        }
    }

    // ---------- janela "Redes encontradas": a pessoa clica numa e confirma ----------
    class NetPicker : DarkForm
    {
        public int Chosen = -1;   // -1 = segundo botao (criar nova / inserir convite) ou fechou
        public NetPicker(IList<string> items, string other) : base(380)
        {
            int pad = D(20), w = ClientSize.Width - pad * 2, y = ContentTop + D(16);
            var title = new Label { Text = L.T("Redes encontradas", "Networks found"), Font = Theme.Title(11), ForeColor = Theme.Gold, BackColor = Theme.Bg, Location = new Point(pad, y), Size = new Size(w, D(26)) };
            Controls.Add(title); y = title.Bottom + D(10);
            int bw = (w - D(12)) / 2;
            var ok = new DarkButton { Text = L.T("Confirmar", "Confirm"), Glyph = "", Style = BtnStyle.Primary, Size = new Size(bw, D(42)), Dim = true };
            var rows = new List<DarkButton>();
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                var r = new DarkButton { Text = items[i], Font = Theme.Body(9.5f), Location = new Point(pad, y), Size = new Size(w, D(38)) };
                r.Click += (s, e) => { Chosen = idx; foreach (var x in rows) { x.Style = x == r ? BtnStyle.Gold : BtnStyle.Normal; x.Invalidate(); } ok.Dim = false; };
                r.DoubleClick += (s, e) => { Chosen = idx; DialogResult = DialogResult.OK; Close(); };
                rows.Add(r); Controls.Add(r); y = r.Bottom + D(6);
            }
            y += D(10);
            ok.Location = new Point(pad, y);
            var no = new DarkButton { Text = other, Location = new Point(pad + bw + D(12), y), Size = new Size(bw, D(42)) };
            ok.Click += (s, e) => { if (Chosen < 0) return; DialogResult = DialogResult.OK; Close(); };
            no.Click += (s, e) => { Chosen = -1; Close(); };
            Controls.Add(ok); Controls.Add(no);
            FinishHeight(ok.Bottom + pad);
        }
        // mostra a janela; devolve o indice escolhido ou -1
        public static int Ask(IWin32Window owner, IList<string> items, string other)
        {
            using (var p = new NetPicker(items, other)) { p.StartPosition = FormStartPosition.CenterParent; return p.ShowDialog(owner) == DialogResult.OK ? p.Chosen : -1; }
        }
    }

    // ---------- servidores RadminBell ligados na rede do Radmin (guest acha o host sozinho) ----------
    static class Finder
    {
        // pergunta "quem e host?" pela rede do Radmin (broadcast UDP); cada RadminBell Host responde com IP e rede
        public static List<KeyValuePair<string, string>> Discover(string ownIp)
        {
            var res = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(ownIp) || !ownIp.StartsWith("26.")) return res;
            try
            {
                using (var u = new UdpClient(new IPEndPoint(IPAddress.Parse(ownIp), 0)))
                {
                    u.EnableBroadcast = true;
                    var q = Encoding.ASCII.GetBytes("RBDISC1");
                    u.Send(q, q.Length, new IPEndPoint(IPAddress.Parse("26.255.255.255"), Players.Port));
                    foreach (var ip in Util.NeighborIps().Where(x => x != ownIp))
                        try { u.Send(q, q.Length, new IPEndPoint(IPAddress.Parse(ip), Players.Port)); } catch { }
                    var end = DateTime.Now.AddMilliseconds(1500);
                    while (DateTime.Now < end)
                    {
                        u.Client.ReceiveTimeout = Math.Max(1, (int)(end - DateTime.Now).TotalMilliseconds);
                        IPEndPoint from = null; byte[] b;
                        try { b = u.Receive(ref from); } catch { break; }
                        var p = Encoding.UTF8.GetString(b).Split('\n');
                        if (p.Length >= 3 && p[0] == "RBHOST1" && p[1].StartsWith("26.") && p[1] != ownIp && !res.Any(x => x.Key == p[1]))
                            res.Add(new KeyValuePair<string, string>(p[1], p[2]));
                    }
                }
            }
            catch { }
            return res;
        }

        public static List<string> Servers(string ownIp)
        {
            var ips = Util.NeighborIps().Where(x => x != ownIp).ToList();
            var found = new List<string>(); var lk = new object();
            var ths = ips.Select(ip => new Thread(() => { if (Util.HttpOk("http://" + ip + ":31315/status", 1500)) lock (lk) found.Add(ip); }) { IsBackground = true }).ToList();
            foreach (var t in ths) t.Start();
            foreach (var t in ths) t.Join(2500);
            lock (lk) return found.OrderBy(x => x).ToList();
        }
    }

    // ---------- passo a passo do Radmin (o Radmin VPN nao aceita comandos: a pessoa cola nome e senha;
    // o RadminBell percebe sozinho pelo service.log quando a rede foi criada / a pessoa entrou) ----------
    class RadminGuide : DarkForm
    {
        public bool Done;
        readonly Label state;
        readonly string net, pass;
        readonly bool creating;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1200 };

        public RadminGuide(bool create, string netName, string pass) : base(430)
        {
            net = netName; this.pass = pass; creating = create;
            int pad = D(22), w = ClientSize.Width - pad * 2, y = ContentTop + D(16);
            var title = new Label { Text = create ? L.T("Criar a rede no Radmin VPN", "Create the network in Radmin VPN") : L.T("Entrar na rede no Radmin VPN", "Join the network in Radmin VPN"),
                                    Font = Theme.Title(11), ForeColor = Theme.Gold, BackColor = Theme.Bg, Location = new Point(pad, y), Size = new Size(w, D(26)) };
            Controls.Add(title); y = title.Bottom + D(8);
            string steps = create
                ? L.T("1. No Radmin VPN: Rede > Criar rede\n2. Nome da rede: cole (Ctrl+V) - já foi copiado\n3. Senha: clique em 'Copiar senha' e cole\n4. Clique em Criar no Radmin",
                      "1. In Radmin VPN: Network > Create network\n2. Network name: paste (Ctrl+V) - already copied\n3. Password: click 'Copy password' and paste\n4. Click Create in Radmin")
                : L.T("1. No Radmin VPN: Rede > Entrar numa rede existente\n2. Nome da rede: cole (Ctrl+V) - já foi copiado\n3. Senha: clique em 'Copiar senha' e cole\n4. Clique em Entrar no Radmin",
                      "1. In Radmin VPN: Network > Join an existing network\n2. Network name: paste (Ctrl+V) - already copied\n3. Password: click 'Copy password' and paste\n4. Click Join in Radmin");
            var sf = Theme.Body(9.5f);
            var ss = TextRenderer.MeasureText(steps, sf, new Size(w, 0), TextFormatFlags.WordBreak);
            Controls.Add(new Label { Text = steps, Font = sf, ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(pad, y), Size = new Size(w, ss.Height + D(6)) });
            y += ss.Height + D(14);
            int bw = (w - D(12)) / 2;
            var cn = new DarkButton { Text = L.T("Copiar nome", "Copy name"), Glyph = "\uE8C8", Style = BtnStyle.Small, Font = Theme.Body(9), Location = new Point(pad, y), Size = new Size(bw, D(34)) };
            var cpw = new DarkButton { Text = L.T("Copiar senha", "Copy password"), Glyph = "\uE8C8", Style = BtnStyle.Small, Font = Theme.Body(9), Location = new Point(pad + bw + D(12), y), Size = new Size(bw, D(34)) };
            cn.Click += (s, e) => Clipboard.SetText(netName);
            cpw.Click += (s, e) => { if (!string.IsNullOrEmpty(pass)) Clipboard.SetText(pass); };
            Controls.Add(cn); Controls.Add(cpw); y = cn.Bottom + D(12);
            var info = new StatusBox { Location = new Point(pad, y), Size = new Size(w, D(10) * 2 + D(21) * 2) };
            info.Lines = new List<StatusBox.Run[]> {
                new[] { new StatusBox.Run(L.T("Nome: ", "Name: "), Theme.Muted), new StatusBox.Run(netName, Theme.Text, true) },
                new[] { new StatusBox.Run(L.T("Senha: ", "Password: "), Theme.Muted), new StatusBox.Run(string.IsNullOrEmpty(pass) ? "-" : pass, Theme.Text, true) } };
            Controls.Add(info); y = info.Bottom + D(12);
            state = new Label { Text = L.T("Aguardando o Radmin VPN...", "Waiting for Radmin VPN..."), Font = Theme.Body(9.5f, FontStyle.Bold), ForeColor = Theme.Gold, BackColor = Theme.Bg,
                                TextAlign = ContentAlignment.MiddleCenter, Location = new Point(pad, y), Size = new Size(w, D(24)) };
            Controls.Add(state); y = state.Bottom + D(12);
            var open = new DarkButton { Text = L.T("Abrir Radmin VPN", "Open Radmin VPN"), Glyph = "\uE71B", Location = new Point(pad, y), Size = new Size(bw, D(40)) };
            var skip = new DarkButton { Text = L.T("Já fiz, continuar", "Done, continue"), Style = BtnStyle.Outline, Location = new Point(pad + bw + D(12), y), Size = new Size(bw, D(40)) };
            open.Click += (s, e) => Radmin.Open();
            skip.Click += (s, e) => { Done = true; Close(); };
            Controls.Add(open); Controls.Add(skip);
            FinishHeight(open.Bottom + pad);
            timer.Tick += (s, e) => Check();
            Shown += (s, e) =>
            {
                try { Clipboard.SetText(netName); } catch { }
                if (!Radmin.Open()) state.Text = L.T("Radmin VPN não está instalado: o site dele foi aberto. Instale e volte aqui.", "Radmin VPN is not installed: its website was opened. Install it and come back.");
                timer.Start();
            };
            FormClosed += (s, e) => timer.Stop();
        }
        void Check()
        {
            var st = Radmin.Read();
            if (!st.Networks.Contains(net)) return;
            timer.Stop(); Done = true;
            SavedNets.Save(net, pass);   // criada (host) ou entrou pelo convite (guest) pelo passo a passo: a senha fica guardada
            state.Text = L.T("✓ Pronto: rede '" + net + "' no Radmin VPN", "✓ Done: network '" + net + "' in Radmin VPN"); state.ForeColor = Theme.Green;
            var t = new System.Windows.Forms.Timer { Interval = 1200 };
            t.Tick += (s, e) => { t.Stop(); Close(); };
            t.Start();
        }
    }

    // ---------- links do rodape (iguais no Host e no Guest) ----------
    static class Footer
    {
        // devolve a altura usada
        public static int Add(DarkForm f, int pad, int w, int y, bool host)
        {
            int lw = (w - f.D(12)) / 2;
            var role = new LinkText { Text = host ? L.T("Trocar para Guest", "Switch to Guest") : L.T("Trocar para Host", "Switch to Host"), Location = new Point(pad, y), Size = new Size(lw, f.D(20)) };
            role.Click += (s, e) => Program.Restart(host ? "guest" : "host");
            var coop = new LinkText { Text = "Co-op: " + Coop.Name, Location = new Point(pad + lw + f.D(12), y), Size = new Size(lw, f.D(20)) };
            coop.Click += (s, e) =>
            {
                bool next = !Coop.Seamless;
                if (!Msg.Ask(f, L.T("Trocar o co-op para '" + (next ? "Seamless Co-op" : "Bloodborne normal") + "'?\n\nSeamless Co-op = pacote BBSC (o RadminBell muda o account.dat do Seamless e o launcher).\nBloodborne normal = sem mod: só a página Online do launcher da BBPort.",
                                    "Switch co-op to '" + (next ? "Seamless Co-op" : "Regular Bloodborne") + "'?\n\nSeamless Co-op = BBSC package (RadminBell changes the Seamless account.dat and the launcher).\nRegular Bloodborne = no mod: only the BBPort launcher Online page."),
                                 L.T("Trocar", "Switch"))) return;
                if (next && Switch.Root == null && !Switch.FindRoot(true)) return;
                Coop.Set(next);
                Program.Restart(host ? "host" : "guest");
            };
            f.Controls.Add(role); f.Controls.Add(coop);
            y += f.D(24);
            int extra = 0;
            if (!host)
            {
                var folder = new LinkText { Text = Coop.Seamless ? L.T("Pasta do Seamless Co-op", "Seamless Co-op folder") : L.T("Pasta do Bloodborne", "Bloodborne folder"), Location = new Point(pad, y), Size = new Size(lw, f.D(20)) };
                folder.Click += (s, e) => { if (Coop.Seamless ? Switch.FindRoot(true) : Switch.FindBBPort()) Program.Restart("guest"); };
                f.Controls.Add(folder);
            }
            var lang = new LinkText { Text = "Language / Idioma", Location = host ? new Point(pad, y) : new Point(pad + lw + f.D(12), y), Size = host ? new Size(w, f.D(20)) : new Size(lw, f.D(20)) };
            lang.Click += (s, e) => { if (L.Pick() != "") Program.Restart(host ? "host" : "guest"); };
            f.Controls.Add(lang);
            extra = f.D(20);
            return f.D(24) + extra;
        }
    }

    static class Program
    {
        static string next;          // papel da proxima tela (trocar Host/Guest, idioma, co-op)
        static DarkForm current;
        static NotifyIcon tray;
        static bool trayTipShown;

        public static void Restart(string role)
        {
            // saindo do Host para o Guest com o servidor ligado: o Guest nao controla o servidor -> desliga (perguntando)
            if (current is HostForm && role == "guest" && Server.AnyRunning)
            {
                if (!Msg.Ask(current, L.T("Trocar para Guest desliga o servidor (quem estiver jogando cai). Continuar?",
                                          "Switching to Guest stops the server (anyone playing is dropped). Continue?"),
                             L.T("Desligar e trocar", "Stop and switch"), L.T("Cancelar", "Cancel"))) return;
                Server.Stop();
            }
            next = role;
            DarkForm.Exiting = true;
            if (current != null) current.Close();
        }
        public static void ShowMain()
        {
            if (current == null || current.IsDisposed) return;
            current.Show(); if (current.WindowState == FormWindowState.Minimized) current.WindowState = FormWindowState.Normal;
            current.Activate();
        }
        // fechar de verdade (menu do icone): com o servidor ligado, pergunta e desliga junto
        static void Quit()
        {
            if (Server.AnyRunning)
            {
                ShowMain();
                if (!Msg.Ask(current, L.T("Fechar o RadminBell também desliga o servidor (quem estiver jogando cai). Fechar?",
                                          "Closing RadminBell also stops the server (anyone playing is dropped). Close?"),
                             L.T("Fechar e desligar", "Close and stop"), L.T("Cancelar", "Cancel"))) return;
                Server.Stop();
            }
            DarkForm.Exiting = true; next = null;
            if (current != null) current.Close();
        }

        static string PickRole()
        {
            using (var f = new DarkForm(340))
            {
                string res = "";
                int pad = f.D(20), w = f.ClientSize.Width - pad * 2, y = f.ContentTop + f.D(22);
                var host = new DarkButton { Text = L.T("Sou o host", "I'm the host"), Style = BtnStyle.Primary, Font = Theme.Title(11), Location = new Point(pad, y), Size = new Size(w, f.D(52)) };
                y = host.Bottom + f.D(12);
                var guest = new DarkButton { Text = L.T("Sou o guest", "I'm the guest"), Font = Theme.Title(11), Location = new Point(pad, y), Size = new Size(w, f.D(52)) };
                host.Click += (s, e) => { res = "host"; f.Close(); };
                guest.Click += (s, e) => { res = "guest"; f.Close(); };
                f.Controls.Add(host); f.Controls.Add(guest);
                f.FinishHeight(guest.Bottom + f.D(22));
                f.ShowDialog();
                return res;
            }
        }

        static void MakeTray()
        {
            tray = new NotifyIcon { Text = "RadminBell", Visible = true };
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
            var menu = new ContextMenuStrip();
            menu.Opening += (s, e) =>
            {
                menu.Items.Clear();
                menu.Items.Add(L.T("Abrir RadminBell", "Open RadminBell"), null, (a, b) => ShowMain());
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(L.T("Fechar o RadminBell", "Close RadminBell"), null, (a, b) => Quit());
                e.Cancel = false;
            };
            menu.Items.Add("-");
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        }

        static bool WaitMutex(Mutex m) { try { return m.WaitOne(10000); } catch (AbandonedMutexException) { return true; } }

        [STAThread]
        static int Main(string[] args)
        {
            Paths.Init();
            L.Load();
            Coop.Load();
            string a = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (a == "--setup") { try { Server.SetupFirewall(); return 0; } catch { return 1; } }
            if (a == "--stop") { Server.Stop(); return 0; }
            if (a == "--start") return 0;   // inicio com o Windows saiu; versao antiga pode ter deixado isso no logon
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, "RadminBell-Show"))
            using (var m = new Mutex(true, "RadminBell-App", out bool first))
            {
                // ja aberto: so mostra a janela que esta la (reaberto como admin: espera a antiga fechar)
                if (!first && !(a == "--elevated" && WaitMutex(m))) { show.Set(); return 0; }
                if (Paths.TestMode && a != "--elevated") { Paths.Init(true); L.Load(); Coop.Load(); }   // modo teste: sempre como na 1a vez
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (L.Lang == "" && L.Pick() == "") return 0;
                Switch.FindRoot(false);
                string role = Paths.ReadLine1(Paths.RoleFile);
                if (role != "host" && role != "guest") { role = PickRole(); if (role == "") return 0; }
                MakeTray();
                var waiter = new Thread(() => { while (show.WaitOne()) { var c = current; if (c != null && !c.IsDisposed) try { c.BeginInvoke((Action)ShowMain); } catch { } } }) { IsBackground = true };
                waiter.Start();
                while (role != null)
                {
                    Paths.WriteText(Paths.RoleFile, role);
                    next = null; DarkForm.Exiting = false;
                    // Guest nao controla servidor: sobra de um app fechado a forca (ex.: Gerenciador de Tarefas) e desligada
                    if (role == "guest" && Server.AnyRunning) Server.Stop();
                    current =role == "host" ? (DarkForm)new HostForm() : new GuestForm();
                    current.CloseToTray = true;
                    current.VisibleChanged += (s, e) =>
                    {
                        var c = (DarkForm)s;
                        if (!c.Visible && !DarkForm.Exiting && !trayTipShown)
                        {
                            trayTipShown = true;
                            tray.ShowBalloonTip(3000, "RadminBell", L.T("O RadminBell continua aberto aqui. Para fechar: botão direito no ícone > Fechar.", "RadminBell is still running here. To close it: right-click the icon > Close."), ToolTipIcon.Info);
                        }
                    };
                    Application.Run(current);
                    role = next;
                }
                // o app esta saindo de vez (fechar pelo icone, ou Windows desligando/saindo): o servidor nao fica rodando sozinho
                if (Server.AnyRunning) Server.Stop();
                tray.Visible = false; tray.Dispose();
            }
            return 0;
        }
    }
}
