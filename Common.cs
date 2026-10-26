// RadminBell - parte comum: tema escuro, controles desenhados, idioma, pastas,
// Radmin VPN (IP, ligado/desligado, redes e amigos pelo service.log), rede local e JSON.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Reflection;

// informacoes de versao do exe (Propriedades > Detalhes)
[assembly: AssemblyProduct("RadminBell - Private Bloodborne Co-op Server")]
[assembly: AssemblyCompany("RadminBell")]
[assembly: AssemblyCopyright("Copyright (c) 2026 RadminBell - MIT License")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]

namespace RadminBell
{
    static class Theme
    {
        public static readonly Color Bg = Hex("#080808"), Top = Hex("#050505"), Panel = Hex("#121212"), Input = Hex("#0d0d0d"),
            StatusBg = Hex("#030303"), Blood = Hex("#8A0303"), BloodHi = Hex("#b30404"), BloodText = Hex("#d33a3a"),
            Gold = Hex("#bda065"), Text = Hex("#d4d4d4"), Muted = Hex("#7a7a7a"), Border = Hex("#2a2a2a"),
            Green = Hex("#7bb08c"), GreenBorder = Hex("#4a7c59"), GreenBg = Hex("#111a14"), RedBg = Hex("#181010"),
            Orange = Hex("#d0a050");
        public static Color Hex(string h) { return ColorTranslator.FromHtml(h); }
        public static Font Body(float s = 10f, FontStyle st = FontStyle.Regular) { return new Font("Segoe UI", s, st); }
        public static Font Mono(float s = 9.5f, FontStyle st = FontStyle.Regular) { return new Font(Has("Cascadia Mono") ? "Cascadia Mono" : "Consolas", s, st); }
        public static Font Title(float s = 10.5f) { return new Font(Has("Cinzel") ? "Cinzel" : "Palatino Linotype", s, FontStyle.Bold); }
        public static Font Icon(float s = 11f) { return new Font(Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", s); }
        static HashSet<string> fonts;
        static bool Has(string f)
        {
            if (fonts == null) using (var c = new InstalledFontCollection()) fonts = new HashSet<string>(c.Families.Select(x => x.Name));
            return fonts.Contains(f);
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (rad <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }

        // imagens dentro do exe sem compressao (imagens.ps1): int32 largura, int32 altura, pixels BGRA
        static readonly Dictionary<string, Bitmap> imgs = new Dictionary<string, Bitmap>();
        public static Bitmap Img(string name)
        {
            Bitmap b;
            if (imgs.TryGetValue(name, out b)) return b;
            using (var s = typeof(Theme).Assembly.GetManifestResourceStream(name + ".rgba"))
            using (var r = new BinaryReader(s))
            {
                int w = r.ReadInt32(), h = r.ReadInt32();
                var px = r.ReadBytes(w * h * 4);
                b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var d = b.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                for (int y = 0; y < h; y++) Marshal.Copy(px, y * w * 4, d.Scan0 + y * d.Stride, w * 4);
                b.UnlockBits(d);
            }
            imgs[name] = b;
            return b;
        }
        public static Image Logo { get { return Img("logo"); } }
    }

    // ---------- idioma ----------
    static class L
    {
        public static string Lang = "";
        public static string T(string pt, string en) { return Lang == "en" ? en : pt; }
        public static void Load() { Lang = File.Exists(Paths.LangFile) ? File.ReadAllText(Paths.LangFile).Trim() : ""; }
        // 1a vez (ou botao Language / Idioma): escolhe e grava; "" = fechou sem escolher
        public static string Pick()
        {
            using (var f = new DarkForm(340, "RadminBell"))
            {
                string res = "";
                int y = f.ContentTop + f.D(8);
                var l = new Label { Text = "Language / Idioma", ForeColor = Theme.Gold, Font = Theme.Title(11), TextAlign = ContentAlignment.MiddleCenter,
                                    Location = new Point(f.D(20), y), Size = new Size(f.ClientSize.Width - f.D(40), f.D(28)), BackColor = Color.Transparent };
                f.Controls.Add(l); y += f.D(40);
                int w = (f.ClientSize.Width - f.D(52)) / 2;
                var en = new DarkButton { Text = "English", Location = new Point(f.D(20), y), Size = new Size(w, f.D(46)) };
                var pt = new DarkButton { Text = "Português (Brasil)", Location = new Point(f.D(32) + w, y), Size = new Size(w, f.D(46)) };
                en.Click += (s, e) => { res = "en"; f.Close(); };
                pt.Click += (s, e) => { res = "pt"; f.Close(); };
                f.Controls.Add(en); f.Controls.Add(pt);
                f.FinishHeight(y + f.D(46) + f.D(20));
                f.ShowDialog();
                if (res != "") { File.WriteAllText(Paths.LangFile, res); Lang = res; }
                return res;
            }
        }
    }

    // ---------- pastas e arquivos do RadminBell ----------
    static class Paths
    {
        // modo teste: com MODO-TESTE.txt ao lado do exe, a memoria do RadminBell fica em dados-teste\ e e zerada a
        // cada abertura (sempre como na 1a vez). O backup do servidor anterior (Voltar) nunca e apagado.
        static readonly string RealData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadminBell");
        public static readonly bool TestMode = File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "MODO-TESTE.txt"));
        public static readonly string Data = TestMode ? Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "dados-teste") : RealData;
        static readonly string[] Keep = { "launcher-anterior.json", "account-radmin.dat", "redes-criadas.json" };
        public static string SavedNetsFile { get { return Path.Combine(Data, "redes-criadas.json"); } }   // redes feitas pelo RadminBell (nome + senha)
        public static string LangFile { get { return Path.Combine(Data, "idioma.txt"); } }
        public static string NetFile { get { return Path.Combine(Data, "rede-radmin.txt"); } }
        public static string HostFile { get { return Path.Combine(Data, "servidor-radmin.txt"); } }
        public static string SeamlessFile { get { return Path.Combine(Data, "pasta-seamless.txt"); } }
        public static string RoleFile { get { return Path.Combine(Data, "papel.txt"); } }      // host / guest
        public static string BBPortFile { get { return Path.Combine(Data, "pasta-bbport.txt"); } }  // Bloodborne normal: pasta com o Bloodborne.exe
        public static string CoopFile { get { return Path.Combine(Data, "coop.txt"); } }       // seamless / normal
        public static string InviteFile { get { return Path.Combine(Data, "convite.txt"); } }  // ultimo convite colado (guest)
        public static string AppDir { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
        public static string Server { get { return Path.Combine(AppDir, "server"); } }
        public static string RepairFile { get { return Path.Combine(Data, "reparo-servidor.txt"); } }   // quando o servidor foi reparado
        public static string Logs { get { return Path.Combine(Server, "logs"); } }

        public static void Init(bool fresh = false)
        {
            if (TestMode)
            {
                Directory.CreateDirectory(Data);
                if (fresh)
                    foreach (var f in Directory.GetFiles(Data))
                        if (!Keep.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }
                // o backup do servidor anterior da memoria normal vale aqui tambem (senao o Voltar nao saberia voltar)
                foreach (var k in Keep)
                {
                    string a = Path.Combine(RealData, k), b = Path.Combine(Data, k);
                    if (File.Exists(a) && !File.Exists(b)) try { File.Copy(a, b); } catch { }
                }
                return;
            }
            string old = Path.Combine(Path.GetDirectoryName(Data), "BRHunt-Radmin");   // nome antigo do app
            if (Directory.Exists(old) && !Directory.Exists(Data)) CopyDir(old, Data);
            Directory.CreateDirectory(Data);
        }
        static void CopyDir(string a, string b)
        {
            Directory.CreateDirectory(b);
            foreach (var f in Directory.GetFiles(a)) File.Copy(f, Path.Combine(b, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(a)) CopyDir(d, Path.Combine(b, Path.GetFileName(d)));
        }
        public static string ReadLine1(string f) { try { return File.Exists(f) ? (File.ReadAllLines(f).FirstOrDefault() ?? "").Trim() : ""; } catch { return ""; } }
        public static void WriteText(string f, string t) { File.WriteAllText(f, t, new UTF8Encoding(false)); }
    }

    // ---------- Radmin VPN ----------
    // IP: fixo por PC (registro IPv4, so muda se o Radmin for reinstalado do zero). Ligado: registro PowerOn.
    // Redes: o Radmin nao guarda os nomes no registro -> lidos do service.log (criou/entrou/apagou/saiu).
    class RadminState
    {
        public bool Installed, PowerOn, ConnectedToServer, LogFound;
        public string Ip = "";
        public HashSet<string> Networks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // criadas ou entradas
        public HashSet<string> Owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Peers = new List<string>();   // amigos conectados agora
    }

    static class Radmin
    {
        const string Key = @"SOFTWARE\Famatech\RadminVPN\1.0";
        static readonly string LogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Famatech\Radmin VPN\service.log");
        static long logLen = -1; static DateTime logTime;
        static RadminState cached;

        public static string Gui()
        {
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles" })
            {
                var d = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrEmpty(d)) continue;
                var exe = Path.Combine(d, @"Radmin VPN\RvRvpnGui.exe");
                if (File.Exists(exe)) return exe;
            }
            return null;
        }
        public static bool Open()
        {
            var exe = Gui();
            // pasta atual = a do Radmin: o Radmin fica rodando e nao pode segurar a pasta do RadminBell (impede apagar)
            if (exe != null) { Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = true }).Dispose(); return true; }
            Util.OpenUrl("https://www.radmin-vpn.com");
            return false;
        }

        public static RadminState Read()
        {
            // instalado = o programa existe (desinstalar deixa o registro e o log para tras);
            // ligado = a placa do Radmin esta ativa com IP 26.x (e o Radmin nao foi desligado no botao)
            var st = new RadminState { Installed = Gui() != null };
            if (!st.Installed) return st;
            bool regOn = true;
            try
            {
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(Key))
                {
                    if (hk != null)
                    {
                        object ip = hk.GetValue("IPv4"), on = hk.GetValue("PowerOn");
                        if (ip is int) { uint v = unchecked((uint)(int)ip); st.Ip = string.Format("{0}.{1}.{2}.{3}", v >> 24, (v >> 16) & 255, (v >> 8) & 255, v & 255); }
                        regOn = on is int ? (int)on != 0 : true;
                    }
                }
            }
            catch { }
            string live = AdapterIp();
            if (live != null) st.Ip = live;
            st.PowerOn = regOn && live != null;
            ReadLog(st);
            return st;
        }

        static string AdapterIp()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.Name != "Radmin VPN" && !ni.Description.StartsWith("Famatech Radmin VPN")) continue;
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && a.Address.ToString().StartsWith("26.")) return a.Address.ToString();
                }
            }
            catch { }
            return null;
        }

        static readonly Regex Line = new Regex(@"^\S+ \S+\t\w+\t(.*)$");
        static readonly Regex ReqCreate = new Regex(@"^\[req:(\d+)\] Requested to (create|join) network '(.*)'");
        static readonly Regex ReqDone = new Regex(@"^\[req:(\d+)\] (Succeeded|Failed|Request dropped)");
        static readonly Regex Gone = new Regex(@"^You (deleted|left) network '(.*)'");
        // rede apagada pelo dono (outro PC) ou voce removido dela: tambem sai da lista
        static readonly Regex GoneByOther = new Regex(@"^Network '(.*)' was (deleted|removed)|^You (were|have been) (kicked|removed|banned|expelled)\b.*network '(.*)'");
        static readonly Regex Joined = new Regex(@"^You joined network '(.*)'");
        static readonly Regex PeerUp = new Regex(@"^Connected to (\d+)/'(.*)' via");
        static readonly Regex PeerDown = new Regex(@"^Node (\d+)/'?(.*?)'? disconnected");

        static void ReadLog(RadminState st)
        {
            try
            {
                var fi = new FileInfo(LogFile);
                if (!fi.Exists) return;
                st.LogFound = true;
                if (cached != null && fi.Length == logLen && fi.LastWriteTimeUtc == logTime) { Copy(cached, st); return; }
                string text;
                using (var fs = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.Unicode, true)) text = sr.ReadToEnd();
                var nets = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pending = new Dictionary<string, KeyValuePair<string, string>>();
                var peers = new Dictionary<string, string>();
                bool server = false;
                foreach (var raw in text.Split('\n'))
                {
                    var m = Line.Match(raw.TrimEnd('\r'));
                    if (!m.Success) continue;
                    string s = m.Groups[1].Value; Match x;
                    if (s.StartsWith("Connected to server")) server = true;
                    else if (s.StartsWith("Disconnected from server") || s == "Switched Off" || s.StartsWith("Service stopping")) { server = false; peers.Clear(); }
                    else if ((x = ReqCreate.Match(s)).Success) pending[x.Groups[1].Value] = new KeyValuePair<string, string>(x.Groups[2].Value, x.Groups[3].Value);
                    else if ((x = ReqDone.Match(s)).Success)
                    {
                        KeyValuePair<string, string> p;
                        if (pending.TryGetValue(x.Groups[1].Value, out p) && x.Groups[2].Value == "Succeeded")
                        { nets.Add(p.Value); if (p.Key == "create") owned.Add(p.Value); }
                        pending.Remove(x.Groups[1].Value);
                    }
                    else if ((x = Gone.Match(s)).Success) { nets.Remove(x.Groups[2].Value); owned.Remove(x.Groups[2].Value); }
                    else if ((x = GoneByOther.Match(s)).Success)
                    {
                        string n = x.Groups[1].Success && x.Groups[1].Value != "" ? x.Groups[1].Value : x.Groups[5].Value;
                        nets.Remove(n); owned.Remove(n);
                    }
                    else if ((x = Joined.Match(s)).Success) nets.Add(x.Groups[1].Value);
                    else if ((x = PeerUp.Match(s)).Success) peers[x.Groups[1].Value] = x.Groups[2].Value;
                    else if ((x = PeerDown.Match(s)).Success) peers.Remove(x.Groups[1].Value);
                }
                cached = new RadminState { ConnectedToServer = server, Networks = nets, Owned = owned, Peers = peers.Values.Distinct().ToList() };
                logLen = fi.Length; logTime = fi.LastWriteTimeUtc;
                Copy(cached, st);
            }
            catch { }
        }
        static void Copy(RadminState a, RadminState b) { b.ConnectedToServer = a.ConnectedToServer; b.Networks = a.Networks; b.Owned = a.Owned; b.Peers = a.Peers; }

        // linha "Radmin VPN:" do status: so o estado do proprio Radmin (a rede tem a linha dela)
        public static List<StatusBox.Run> StatusLine(RadminState st)
        {
            var r = new List<StatusBox.Run> { new StatusBox.Run("Radmin VPN: ", Theme.Muted) };
            if (st == null) r.Add(new StatusBox.Run("...", Theme.Muted));
            else if (!st.Installed) r.Add(new StatusBox.Run(L.T("PRECISA BAIXAR", "NEEDS DOWNLOAD"), Theme.BloodText, true));
            else if (!st.PowerOn || !st.Ip.StartsWith("26.")) r.Add(new StatusBox.Run(L.T("DESLIGADO", "OFF"), Theme.BloodText, true));
            else if (st.LogFound && !st.ConnectedToServer) r.Add(new StatusBox.Run(L.T("CONECTANDO...", "CONNECTING..."), Theme.Gold, true));
            else r.Add(new StatusBox.Run(L.T("LIGADO", "ON"), Theme.Green, true));
            return r;
        }
    }

    // ---------- utilidades ----------
    static class Util
    {
        public static void OpenUrl(string u) { try { Process.Start(new ProcessStartInfo(u) { UseShellExecute = true }); } catch { } }
        public static bool HttpOk(string url, int ms)
        {
            try
            {
                var rq = (HttpWebRequest)WebRequest.Create(url);
                rq.Timeout = ms; rq.ReadWriteTimeout = ms; rq.Proxy = null;
                using (var r = (HttpWebResponse)rq.GetResponse()) return r.StatusCode == HttpStatusCode.OK;
            }
            catch { return false; }
        }
        public static string HttpGet(string url, int ms)
        {
            try
            {
                var rq = (HttpWebRequest)WebRequest.Create(url);
                rq.Timeout = ms; rq.ReadWriteTimeout = ms; rq.Proxy = null;
                using (var r = (HttpWebResponse)rq.GetResponse())
                using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return r.StatusCode == HttpStatusCode.OK ? sr.ReadToEnd() : null;
            }
            catch { return null; }
        }
        public static bool Running(string name) { var p = Process.GetProcessesByName(name); bool r = p.Length > 0; foreach (var x in p) x.Dispose(); return r; }
        // jogo ou launcher da BBPort abertos (o launcher grava a pagina Online de novo ao fechar)
        public static bool GameOpen() { return Running("Bloodborne") || Running("bb-probe"); }

        // IPs 26.x que o Windows ja viu na placa do Radmin (tabela de vizinhos): candidatos a servidor
        [StructLayout(LayoutKind.Sequential)]
        struct MIB_IPNETROW { public int Index; public int PhysAddrLen; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] Phys; public uint Addr; public int Type; }
        [DllImport("iphlpapi.dll")] static extern int GetIpNetTable(IntPtr table, ref int size, bool sort);
        public static List<string> NeighborIps()
        {
            var res = new List<string>();
            int size = 0;
            GetIpNetTable(IntPtr.Zero, ref size, false);
            if (size <= 0) return res;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetIpNetTable(buf, ref size, false) != 0) return res;
                int n = Marshal.ReadInt32(buf), rs = Marshal.SizeOf(typeof(MIB_IPNETROW));
                for (int i = 0; i < n; i++)
                {
                    var row = (MIB_IPNETROW)Marshal.PtrToStructure(buf + 4 + i * rs, typeof(MIB_IPNETROW));
                    var b = BitConverter.GetBytes(row.Addr);
                    if (b[0] != 26 || b[3] == 255 || b[3] == 0 || row.Type == 2) continue;   // 2 = invalido
                    var ip = b[0] + "." + b[1] + "." + b[2] + "." + b[3];
                    if (!res.Contains(ip)) res.Add(ip);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return res;
        }
        public static bool IsAdmin()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        public static string RandomFrom(string chars, int n)
        {
            var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            var sb = new StringBuilder(); foreach (var x in b) sb.Append(chars[x % chars.Length]); return sb.ToString();
        }
        public static string RandomHex(int bytes)
        {
            var b = new byte[bytes]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return string.Concat(b.Select(x => x.ToString("x2")));
        }
    }

    // ---------- redes do Radmin usadas pelo RadminBell: nome -> senha (protegida pelo Windows, so este usuario le) e IP do host ----------
    // O Radmin nao mostra a senha de rede nenhuma; o app guarda as redes que ele gerou/criou (host) e as que
    // a pessoa entrou pelo convite (guest), e o IP do host em que o guest ja se conectou.
    static class SavedNets
    {
        static Dictionary<string, object> Load() { return Json.Read(Paths.SavedNetsFile) ?? new Dictionary<string, object>(); }
        static Dictionary<string, object> Entry(Dictionary<string, object> d, string name)
        {
            object v; d.TryGetValue(name, out v);
            var e = v as Dictionary<string, object>;
            if (e == null) { e = new Dictionary<string, object>(); if (v is string) e["p"] = v; }   // formato antigo: so a senha
            return e;
        }
        public static void Save(string name, string pass = null, string hostIp = null)
        {
            if (string.IsNullOrEmpty(name) || (string.IsNullOrEmpty(pass) && string.IsNullOrEmpty(hostIp))) return;
            try
            {
                var d = Load(); var e = Entry(d, name);
                if (!string.IsNullOrEmpty(pass)) e["p"] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(pass), null, DataProtectionScope.CurrentUser));
                if (!string.IsNullOrEmpty(hostIp)) e["ip"] = hostIp;
                d[name] = e;
                Json.Write(Paths.SavedNetsFile, d);
            }
            catch { }
        }
        public static string Pass(string name)
        {
            try
            {
                var p = Json.Str(Entry(Load(), name), "p");
                return p == null ? null : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(p), null, DataProtectionScope.CurrentUser));
            }
            catch { return null; }
        }
        public static string HostIp(string name) { try { return Json.Str(Entry(Load(), name), "ip"); } catch { return null; } }
        public static List<string> Names() { return Load().Keys.ToList(); }
    }

    // ---------- JSON (mantem ordem das chaves; escreve indentado) ----------
    static class Json
    {
        static readonly JavaScriptSerializer js = new JavaScriptSerializer();
        public static Dictionary<string, object> Read(string file)
        {
            try { return File.Exists(file) ? js.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)) : null; } catch { return null; }
        }
        public static void Write(string file, Dictionary<string, object> d) { Paths.WriteText(file, Pretty(d, "")); }
        static string Pretty(object o, string ind)
        {
            var d = o as IDictionary<string, object>;
            if (d != null)
            {
                if (d.Count == 0) return "{}";
                var sb = new StringBuilder("{\r\n"); int i = 0;
                foreach (var kv in d)
                    sb.Append(ind + "    " + js.Serialize(kv.Key) + ": " + Pretty(kv.Value, ind + "    ") + (++i < d.Count ? "," : "") + "\r\n");
                return sb.Append(ind + "}").ToString();
            }
            var l = o as IList;
            if (l != null && !(o is string))
            {
                if (l.Count == 0) return "[]";
                var sb = new StringBuilder("[\r\n");
                for (int i = 0; i < l.Count; i++) sb.Append(ind + "    " + Pretty(l[i], ind + "    ") + (i + 1 < l.Count ? "," : "") + "\r\n");
                return sb.Append(ind + "]").ToString();
            }
            return js.Serialize(o);
        }
        public static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? v.ToString() : null; }
    }

    // =====================================================================
    // Controles desenhados (tema do mockup)
    // =====================================================================
    class DarkForm : Form
    {
        public float Scale_ = 1f;
        public int D(float v) { return (int)Math.Round(v * Scale_); }
        public int ContentTop { get { return header.Height; } }
        readonly Panel header;
        readonly string sub;

        // janela principal: o X esconde na area de notificacao (fechar de verdade pelo menu do icone)
        public bool CloseToTray;
        public static bool Exiting;

        // janelas que abrem por cima do app (avisos, redes, passo a passo...): sombra vermelho-escura em volta
        // (3 camadas atras da janela, em degrade) e sempre centralizada sobre o app
        readonly List<Form> glow = new List<Form>();
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (CloseToTray) return;
            var o = Owner ?? Form.ActiveForm;
            if (o != null && o != this && o.Visible && o.WindowState != FormWindowState.Minimized)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(o.Left + (o.Width - Width) / 2, o.Top + (o.Height - Height) / 2);
            }
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (CloseToTray) return;
            foreach (var l in new[] { new[] { 16f, 0.12f }, new[] { 10f, 0.2f }, new[] { 5f, 0.32f } })
                glow.Add(new GlowForm { BackColor = Theme.Blood, Opacity = l[1], Tag = (int)l[0] });
            foreach (var g in glow) { if (Owner != null) g.Owner = Owner; g.Show(); }   // mesmo "pai" da janela: fica no mesmo grupo
            PlaceGlow();
            LocationChanged += (s, a) => PlaceGlow();
            SizeChanged += (s, a) => PlaceGlow();
            Activated += (s, a) => PlaceGlow();
            // depois que tudo apareceu: janela na frente e sombra logo atras dela
            BeginInvoke((Action)(() => { if (IsDisposed) return; Activate(); BringToFront(); PlaceGlow(); }));
        }
        [DllImport("user32")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
        void PlaceGlow()
        {
            // cada camada logo ATRAS desta janela (nunca por cima)
            foreach (var g in glow)
            {
                int m = D((int)g.Tag);
                SetWindowPos(g.Handle, Handle, Left - m, Top - m, Width + m * 2, Height + m * 2, 0x0010);   // SWP_NOACTIVATE
            }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            foreach (var g in glow) { try { g.Close(); g.Dispose(); } catch { } }
            glow.Clear();
            base.OnFormClosed(e);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (CloseToTray && !Exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }

        public DarkForm(int width, string subtitle = null, bool big = false)
        {
            sub = subtitle;
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) Scale_ = g.DpiX / 96f;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body(10);
            DoubleBuffered = true; KeyPreview = true;
            Text = "RadminBell" + (string.IsNullOrEmpty(subtitle) || subtitle == "RadminBell" ? "" : " - " + subtitle);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ClientSize = new Size(D(width), D(200));
            header = new Panel { Location = Point.Empty, Size = new Size(ClientSize.Width, D(big ? 78 : 60)), BackColor = Theme.Top };
            header.Paint += PaintHeader;
            header.MouseDown += Drag;
            Controls.Add(header);
            var close = new HeaderButton("", true) { Size = new Size(D(46), D(34)) };
            close.Location = new Point(header.Width - close.Width, 0); close.Click += (s, e) => Close();
            var min = new HeaderButton("", false) { Size = new Size(D(46), D(34)) };
            min.Location = new Point(close.Left - min.Width, 0); min.Click += (s, e) => WindowState = FormWindowState.Minimized;
            header.Controls.Add(close); header.Controls.Add(min);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && Modal) Close(); };
        }
        public void FinishHeight(int h) { ClientSize = new Size(ClientSize.Width, h); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000 | 0x00080000;   // WS_MINIMIZEBOX | WS_SYSMENU: minimizar pela barra de tarefas
                cp.ClassStyle |= 0x00020000;            // CS_DROPSHADOW
                return cp;
            }
        }
        [DllImport("user32")] static extern bool ReleaseCapture();
        [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        void Drag(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
        void PaintHeader(object s, PaintEventArgs e)
        {
            var g = e.Graphics; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.AntiAlias;
            // so a logo (o papel Host/Guest fica embaixo, como titulo - RoleTitle)
            var img = Theme.Logo;
            int h = D(30), w = h * img.Width / img.Height;
            g.DrawImage(img, new Rectangle(D(20), (header.Height - h) / 2, w, h));
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // borda fina com brilho vermelho (como o mockup)
            using (var p = new Pen(Color.FromArgb(110, Theme.Blood))) e.Graphics.DrawRectangle(p, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    // camada da sombra: nao recebe foco nem clique, nao aparece na barra de tarefas
    class GlowForm : Form
    {
        public GlowForm() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20 | 0x80000 | 0x08000000 | 0x80;   // TRANSPARENT (clique passa) | LAYERED | NOACTIVATE | TOOLWINDOW
                return cp;
            }
        }
    }

    class HeaderButton : Control
    {
        readonly string glyph; readonly bool isClose; bool hover;
        public HeaderButton(string g, bool close)
        {
            glyph = g; isClose = close; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand; BackColor = Theme.Top;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(hover ? (isClose ? Theme.Blood : Theme.Hex("#1a1a1a")) : Theme.Top);
            TextRenderer.DrawText(e.Graphics, glyph, Theme.Icon(8), ClientRectangle, hover ? Color.White : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    enum BtnStyle { Normal, Start, Stop, Gold, Primary, Outline, Small }

    class DarkButton : Control
    {
        public string Glyph; public BtnStyle Style = BtnStyle.Normal; public string SubText;
        // aparece apagado mas continua clicavel (o clique explica o que falta)
        bool dim; public bool Dim { get { return dim; } set { if (dim != value) { dim = value; Invalidate(); } } }
        bool hover, down;
        public DarkButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; BackColor = Color.Transparent; Font = Theme.Body(10); TabStop = true;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent is Card ? Theme.Panel : Parent != null ? Parent.BackColor : Theme.Bg);
            bool h = hover && Enabled && !dim;
            Color bg = Theme.Panel, border = Theme.Border, fore = Theme.Text, icon = Theme.Gold;
            switch (Style)
            {
                case BtnStyle.Normal: if (h) { border = Theme.Blood; bg = Theme.RedBg; fore = Color.White; } break;
                case BtnStyle.Gold: if (h) { border = Theme.Gold; fore = Theme.Gold; } break;
                case BtnStyle.Start: icon = Theme.Green; if (h) { border = Theme.GreenBorder; bg = Theme.GreenBg; fore = Theme.Green; } break;
                case BtnStyle.Stop: icon = Theme.BloodText; if (h) { border = Theme.Blood; bg = Theme.RedBg; fore = Theme.BloodText; } break;
                case BtnStyle.Primary: bg = h ? Theme.BloodHi : Theme.Blood; border = h ? Theme.BloodHi : Theme.Blood; fore = Color.White; icon = Color.White; break;
                case BtnStyle.Outline: bg = Color.Transparent; fore = h ? Theme.Text : Theme.Muted; border = h ? Theme.Hex("#444444") : Theme.Border; icon = fore; break;
                case BtnStyle.Small: bg = Theme.Bg; fore = h ? Theme.Gold : Theme.Muted; border = h ? Theme.Gold : Theme.Border; icon = fore; break;
            }
            if (!Enabled || dim) { fore = Theme.Hex("#4a4a4a"); icon = fore; border = Theme.Hex("#1f1f1f"); bg = Theme.Hex("#0e0e0e"); }
            if (down && Enabled) bg = ControlPaint.Dark(bg, 0.05f);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, 4 * DeviceDpi / 96f))
            {
                if (bg != Color.Transparent) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(Focused && ShowFocusCues ? Theme.Gold : border)) g.DrawPath(p, path);
            }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            using (var f = (Style == BtnStyle.Start || Style == BtnStyle.Stop || Style == BtnStyle.Primary) ? new Font(Font, FontStyle.Bold) : (Font)Font.Clone())
            {
                if (string.IsNullOrEmpty(Text))   // so icone
                {
                    using (var fi = Theme.Icon(Font.Size)) TextRenderer.DrawText(g, Glyph ?? "", fi, ClientRectangle, icon, TextFormatFlags.HorizontalCenter | flags);
                    return;
                }
                var ts = TextRenderer.MeasureText(g, Text, f, Size.Empty, TextFormatFlags.NoPadding);
                int gw = 0, gap = (int)(8 * DeviceDpi / 96f);
                Font fi2 = null;
                if (Glyph != null) { fi2 = Theme.Icon(Font.Size); gw = TextRenderer.MeasureText(g, Glyph, fi2, Size.Empty, TextFormatFlags.NoPadding).Width + gap; }
                int subH = 0; Font fs = null; Size ss = Size.Empty;
                if (!string.IsNullOrEmpty(SubText)) { fs = Theme.Mono(8); ss = TextRenderer.MeasureText(g, SubText, fs, Size.Empty, TextFormatFlags.NoPadding); subH = ss.Height + 2; }
                int x = Math.Max(4, (Width - ts.Width - gw) / 2);
                int y0 = (Height - ts.Height - subH) / 2;
                if (fi2 != null) { TextRenderer.DrawText(g, Glyph, fi2, new Rectangle(x, y0, gw, ts.Height), icon, flags); fi2.Dispose(); }
                TextRenderer.DrawText(g, Text, f, new Rectangle(x + gw, y0, Width - x - gw - 4, ts.Height), fore, flags);
                if (fs != null)
                {
                    TextRenderer.DrawText(g, SubText, fs, new Rectangle(0, y0 + ts.Height + 2, Width, ss.Height), Enabled ? Theme.Muted : fore,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    fs.Dispose();
                }
            }
        }
    }

    // caixa de texto escura com borda (dourada com foco)
    class DarkInput : Panel
    {
        public readonly TextBox Box;
        readonly Label hint;
        // dica apagada quando o campo esta vazio
        public string Placeholder { get { return hint.Text; } set { hint.Text = value; hint.Visible = Box.Text == "" && value != ""; } }
        public DarkInput()
        {
            BackColor = Theme.Input; DoubleBuffered = true;
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Mono(10) };
            hint = new Label { BackColor = Theme.Input, ForeColor = Theme.Hex("#4a4a4a"), Font = Theme.Body(8.5f), AutoEllipsis = true, Cursor = Cursors.IBeam };
            Controls.Add(Box); Controls.Add(hint); hint.BringToFront();
            Box.GotFocus += (s, e) => Invalidate(); Box.LostFocus += (s, e) => Invalidate();
            Box.TextChanged += (s, e) => hint.Visible = Box.Text == "" && hint.Text != "";
            hint.Click += (s, e) => Box.Focus();
            Click += (s, e) => Box.Focus();
        }
        public override string Text { get { return Box.Text; } set { Box.Text = value; hint.Visible = Box.Text == "" && hint.Text != ""; } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(10 * DeviceDpi / 96f);
            Box.Location = new Point(pad, (Height - Box.Height) / 2); Box.Width = Width - pad * 2;
            hint.Location = new Point(pad, 1); hint.Size = new Size(Width - pad * 2, Height - 2); hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Visible = Box.Text == "" && hint.Text != "";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var p = new Pen(Box.Focused ? Theme.Gold : Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    // cartao com titulo em dourado em cima da borda
    class Card : Panel
    {
        public string Title;
        public int TitleH;
        public Card() { BackColor = Theme.Bg; DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int half = TitleH / 2;
            var r = new RectangleF(0.5f, half + 0.5f, Width - 1.5f, Height - half - 1.5f);
            using (var path = Theme.Round(r, 6 * DeviceDpi / 96f))
            {
                using (var b = new SolidBrush(Theme.Panel)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border)) g.DrawPath(p, path);
            }
            using (var f = Theme.Title(9.5f))
            {
                var ts = TextRenderer.MeasureText(g, Title, f, Size.Empty, TextFormatFlags.NoPadding);
                int x = (int)(14 * DeviceDpi / 96f), pad = (int)(8 * DeviceDpi / 96f);
                using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, x - pad, 0, ts.Width + pad * 2, TitleH);
                TextRenderer.DrawText(g, Title, f, new Rectangle(x, (TitleH - ts.Height) / 2, ts.Width + 4, ts.Height), Theme.Gold, TextFormatFlags.NoPadding);
            }
        }
    }

    // caixa de status: linhas com pedacos coloridos
    class StatusBox : Control
    {
        public class Run { public string T; public Color C; public bool B; public Run(string t, Color c, bool b = false) { T = t; C = c; B = b; } }
        public List<Run[]> Lines = new List<Run[]>();
        public StatusBox() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        public int LineH { get { return (int)(21 * DeviceDpi / 96f); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; float s = DeviceDpi / 96f;
            g.Clear(Theme.StatusBg);
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(Color.FromArgb(140, Theme.Blood))) g.FillRectangle(b, 0, 0, Math.Max(2, (int)(2 * s)), Height);
            using (var fr = Theme.Mono(9.5f)) using (var fb = Theme.Mono(9.5f, FontStyle.Bold)) using (var fsm = Theme.Mono(8.5f))
            {
                int y = (int)(10 * s);
                foreach (var line in Lines)
                {
                    int x = (int)(16 * s);
                    foreach (var r in line)
                    {
                        var f = r.B ? fb : fr;
                        var w = TextRenderer.MeasureText(g, r.T, f, Size.Empty, TextFormatFlags.NoPadding).Width;
                        TextRenderer.DrawText(g, r.T, f, new Rectangle(x, y, Width - x - (int)(8 * s), LineH), r.C, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                        x += w;
                    }
                    y += LineH;
                }
            }
        }
    }

    // linha decorativa: transparente -> vermelho -> transparente
    class Divider : Control
    {
        public Divider() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            int y = Height / 2; var r = new Rectangle(0, y - 1, Width, 2);
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Color.Transparent, Color.Transparent, 0f))
            {
                var cb = new ColorBlend { Colors = new[] { Theme.Bg, Color.FromArgb(255, 170, 30, 30), Theme.Bg }, Positions = new[] { 0f, 0.5f, 1f } };
                b.InterpolationColors = cb; e.Graphics.FillRectangle(b, r);
            }
            // losango no meio
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = Width / 2, s = Math.Max(3, Height / 2 - 1);
            using (var p = new SolidBrush(Color.FromArgb(255, 190, 40, 40)))
                e.Graphics.FillPolygon(p, new[] { new Point(cx, y - s), new Point(cx + s, y), new Point(cx, y + s), new Point(cx - s, y) });
        }
    }

    // titulo "Host" / "Guest" (imagem na fonte da logo), centralizado entre a logo e o bloco de status
    class RoleTitle : Control
    {
        readonly string img;
        public RoleTitle(string image) { img = image; SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Theme.Bg);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var b = Theme.Img(img);
            int h = Height, w = h * b.Width / b.Height;
            g.DrawImage(b, new Rectangle((Width - w) / 2, 0, w, h));
        }
    }

    // link pequeno (Language / Idioma)
    class LinkText : Control
    {
        bool hover;
        public LinkText() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); Cursor = Cursors.Hand; Font = Theme.Body(8.5f, FontStyle.Underline); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent.BackColor);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, hover ? Theme.Gold : Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // caixa de selecao desenhada
    class DarkCheck : Control
    {
        bool hover; bool chk;
        public bool Checked { get { return chk; } set { chk = value; Invalidate(); } }
        public event EventHandler CheckedChanged;
        public DarkCheck() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true); Cursor = Cursors.Hand; Font = Theme.Body(9); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; if (CheckedChanged != null) CheckedChanged(this, e); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent.BackColor); float s = DeviceDpi / 96f;
            int bx = (int)(14 * s), by = (Height - bx) / 2;
            var box = new Rectangle(0, by, bx, bx);
            using (var b = new SolidBrush(chk ? Theme.Blood : Theme.Input)) g.FillRectangle(b, box);
            using (var p = new Pen(hover ? Theme.Gold : (chk ? Theme.Blood : Theme.Hex("#444444")))) g.DrawRectangle(p, box);
            if (chk) using (var fi = Theme.Icon(7)) TextRenderer.DrawText(g, "", fi, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(bx + (int)(8 * s), 0, Width - bx, Height), hover ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // ---------- seletor de PASTA do Windows (o mesmo visual do Explorer) ----------
    static class FolderPicker
    {
        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogRCW { }
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint c, IntPtr t); void SetFileTypeIndex(uint i); void GetFileTypeIndex(out uint i);
            void Advise(IntPtr p, out uint c); void Unadvise(uint c);
            void SetOptions(uint fos); void GetOptions(out uint fos);
            void SetDefaultFolder(IShellItem si); void SetFolder(IShellItem si);
            void GetFolder(out IShellItem si); void GetCurrentSelection(out IShellItem si);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string n); void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string n);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string t);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string t); void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string t);
            void GetResult(out IShellItem si);
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv); void GetParent(out IShellItem si);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attr); void Compare(IShellItem si, uint hint, out int order);
        }
        // devolve a pasta escolhida ou null
        public static string Pick(IWin32Window owner, string title)
        {
            try
            {
                var dlg = (IFileDialog)new FileOpenDialogRCW();
                dlg.SetOptions(0x20 | 0x40 | 0x800);   // FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST
                dlg.SetTitle(title);
                if (dlg.Show(owner != null ? owner.Handle : IntPtr.Zero) != 0) return null;
                IShellItem si; dlg.GetResult(out si);
                string path; si.GetDisplayName(0x80058000, out path);   // SIGDN_FILESYSPATH
                return path;
            }
            catch
            {
                using (var f = new FolderBrowserDialog { Description = title }) return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
            }
        }

        // acha a pasta que tem o arquivo 'marker' (ex.: tools\play-bbport.ps1): na escolhida, acima dela ou dentro (ate 3 niveis)
        public static List<string> FindWith(string start, string marker, int depth = 3)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(start) || !Directory.Exists(start)) return res;
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, marker))) { res.Add(d.FullName); return res; }
            var q = new Queue<KeyValuePair<string, int>>(); q.Enqueue(new KeyValuePair<string, int>(start, 0));
            while (q.Count > 0 && res.Count < 20)
            {
                var cur = q.Dequeue();
                if (cur.Value >= depth) continue;
                string[] subs;
                try { subs = Directory.GetDirectories(cur.Key); } catch { continue; }
                foreach (var s in subs)
                {
                    if (File.Exists(Path.Combine(s, marker))) res.Add(s);
                    else q.Enqueue(new KeyValuePair<string, int>(s, cur.Value + 1));
                }
            }
            return res.OrderBy(x => x.Length).ToList();
        }
    }

    // mensagem no mesmo estilo (no lugar do MessageBox branco)
    static class Msg
    {
        public static void Show(IWin32Window owner, string text) { Ask(owner, text, null); }
        // yes != null -> 2 botoes; retorna true se clicou no 1o
        public static bool Ask(IWin32Window owner, string text, string yes, string no = null)
        {
            using (var f = new DarkForm(430))
            {
                bool res = false;
                int pad = f.D(22), w = f.ClientSize.Width - pad * 2, y = f.ContentTop + f.D(18);
                var font = Theme.Body(10);
                var sz = TextRenderer.MeasureText(text, font, new Size(w, 0), TextFormatFlags.WordBreak);
                var l = new Label { Text = text, Font = font, ForeColor = Theme.Text, BackColor = Color.Transparent, Location = new Point(pad, y), Size = new Size(w, sz.Height + f.D(4)) };
                f.Controls.Add(l); y += l.Height + f.D(18);
                int bh = f.D(40);
                if (yes == null)
                {
                    var ok = new DarkButton { Text = "OK", Location = new Point(pad, y), Size = new Size(w, bh), Style = BtnStyle.Gold };
                    ok.Click += (s, e) => f.Close(); f.Controls.Add(ok); f.AcceptButtonClick(ok);
                }
                else
                {
                    int bw = (w - f.D(12)) / 2;
                    var b1 = new DarkButton { Text = yes, Location = new Point(pad, y), Size = new Size(bw, bh), Style = BtnStyle.Primary };
                    var b2 = new DarkButton { Text = no ?? L.T("Cancelar", "Cancel"), Location = new Point(pad + bw + f.D(12), y), Size = new Size(bw, bh) };
                    b1.Click += (s, e) => { res = true; f.Close(); }; b2.Click += (s, e) => f.Close();
                    f.Controls.Add(b1); f.Controls.Add(b2);
                }
                f.FinishHeight(y + bh + pad);
                f.TopMost = owner == null;
                if (owner != null) { f.StartPosition = FormStartPosition.CenterParent; f.ShowDialog(owner); } else f.ShowDialog();
                return res;
            }
        }
        static void AcceptButtonClick(this DarkForm f, DarkButton b) { f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) b.PerformClick(); }; f.Shown += (s, e) => b.Focus(); }
        static void PerformClick(this DarkButton b) { b.GetType(); typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(b, new object[] { EventArgs.Empty }); }
    }
}
