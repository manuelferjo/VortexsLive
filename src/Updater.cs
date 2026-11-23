// Actualización automática desde las Releases de GitHub.
// - Al abrirse (y con "Actualizaciones" en la pestaña Registro) mira si hay una versión más nueva.
// - Si el usuario acepta: descarga el ZIP, comprueba tamaño y huella SHA-256, sustituye el .exe en la
//   MISMA carpeta y con el MISMO nombre (quien venga de StreamDelay sigue con su StreamDelay.exe y sus
//   accesos directos funcionan; el viejo queda como .old por si algo falla) y se vuelve a abrir. VortexsLive.ini no
//   se toca: claves y ajustes se conservan, y los permisos de Windows siguen siendo los de esa carpeta.
// - Nunca actualiza en pleno directo.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
    internal partial class MainForm
    {
        const string UpdateRepo = "manuelferjo/VortexsLive";
        string updateSkip = "";   // versión que el usuario decidió saltarse
        volatile bool updateBusy;

        class UpdateInfo
        {
            public string Tag, Notes, Url, Page, Sha256;
            public Version Ver;
            public long Size;
        }

        static Version ParseVersion(string s)
        {
            s = (s ?? "").Trim().TrimStart('v', 'V');
            if (s.IndexOf('.') < 0) s += ".0";
            Version v;
            return System.Version.TryParse(s, out v) ? v : null;
        }

        // Al abrir: borrar el .exe viejo de una actualización anterior y mirar si hay versión nueva.
        void StartUpdateCheck()
        {
            if (Program.CaptureDir != null) return;
            if (Program.UpdatedTo.Length > 0) AppendLog("Vortexs Live actualizado a la versión " + Program.UpdatedTo + ". Tu configuración se ha conservado.");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string old = Application.ExecutablePath + ".old";
                for (int i = 0; i < 10 && File.Exists(old); i++) { try { File.Delete(old); } catch { Thread.Sleep(1000); } }
                Thread.Sleep(4000); // que arranque todo antes
                CheckForUpdate(false);
            });
        }

        // manual = true: pulsado "Actualizaciones" (avisa también si no hay nada nuevo o si falla).
        void CheckForUpdate(bool manual)
        {
            UpdateInfo info = null;
            string error = null;
            try { info = LatestRelease(); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((Action)delegate
                {
                    Version mine = ParseVersion(Version);
                    if (info == null)
                    {
                        if (manual) Toast("No se pudo comprobar si hay versiones nuevas" + (error != null ? ": " + error : "."), "error");
                        return;
                    }
                    if (mine != null && info.Ver <= mine)
                    {
                        if (manual) Toast(I18n.Tr("Tienes la última versión (") + Version + ").", "ok");
                        return;
                    }
                    AppendLog("Hay una versión nueva de Vortexs Live: " + info.Tag + ".");
                    if (!manual && info.Tag == updateSkip) return;
                    UpdateDialog(info);
                });
            }
            catch { } // la ventana ya se cerró
        }

        UpdateInfo LatestRelease()
        {
            object o = Web.GetJson("https://api.github.com/repos/" + UpdateRepo + "/releases/latest");
            var info = new UpdateInfo
            {
                Tag = J.S(o, "tag_name"), Notes = J.S(o, "body").Trim(), Page = J.S(o, "html_url")
            };
            info.Ver = ParseVersion(info.Tag);
            if (info.Ver == null) throw new Exception("la última versión publicada no tiene un número válido (" + info.Tag + ")");
            foreach (var a in J.L(o, "assets"))
            {
                string name = J.S(a, "name");
                // El ZIP es VortexsLive-x.y.zip. (Las Releases llevan también StreamDelay-…zip para las
                // versiones 3.x, que se llamaban StreamDelay y solo buscan ese nombre.)
                if (!name.StartsWith("VortexsLive", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                info.Url = J.S(a, "browser_download_url");
                info.Size = J.N(a, 0, "size");
                string digest = J.S(a, "digest");
                if (digest.StartsWith("sha256:")) info.Sha256 = digest.Substring(7).ToLowerInvariant();
                break;
            }
            if (string.IsNullOrEmpty(info.Url)) throw new Exception("la versión " + info.Tag + " aún no tiene el ZIP de descarga");
            return info;
        }

        void UpdateDialog(UpdateInfo info)
        {
            if (updateBusy) return;
            Form f = DarkForm("Actualización de Vortexs Live", 460, 400);
            Card card = new Card { Title = "Hay una versión nueva: " + info.Tag, Subtitle = "Tienes la " + Version + ". Tu configuración y tus claves se conservan." };
            Place(f, card, 20f, 18f, 420f, 290f);
            string notes = info.Notes.Length > 0 ? info.Notes.Replace("\r", "") : "Sin notas de la versión.";
            if (notes.Length > 900) notes = notes.Substring(0, 900) + "…";
            TextBox tb = new TextBox
            {
                Multiline = true, ReadOnly = true, TabStop = false, BorderStyle = BorderStyle.None,
                ScrollBars = notes.Split('\n').Length > 11 || notes.Length > 600 ? ScrollBars.Vertical : ScrollBars.None,
                BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Body(8.5f), Text = notes.Replace("\n", "\r\n")
            };
            Place(card, tb, 20f, 62f, 380f, 180f);
            VButton page = new VButton { Kind = ButtonKind.Link, Text = "Ver en GitHub →", FontSize = 8.5f };
            Place(card, page, 20f, 250f, 380f, 26f);
            page.Click += delegate { Http.OpenBrowser(info.Page); };

            VButton skip = new VButton { Kind = ButtonKind.Ghost, Text = "Saltar esta", FontSize = 9.5f };
            VButton later = new VButton { Kind = ButtonKind.Ghost, Text = "Más tarde", FontSize = 9.5f };
            VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "Actualizar ahora", FontSize = 10f };
            Place(f, skip, 20f, 326f, 120f, 44f);
            Place(f, later, 148f, 326f, 120f, 44f);
            Place(f, ok, 276f, 326f, 164f, 44f);
            skip.Click += delegate { updateSkip = info.Tag; SaveConfig(); AppendLog("Actualización " + info.Tag + " saltada (puedes instalarla desde Ajustes → Registro → Actualizaciones)."); f.Close(); };
            later.Click += delegate { f.Close(); };
            ok.Click += delegate
            {
                if (engine.OutputActive)
                {
                    I18n.Show(f, "Estás en directo. Termina la transmisión y vuelve a intentarlo: la actualización reinicia Vortexs Live.", "Actualizar");
                    return;
                }
                f.Close();
                RunUpdate(info);
            };
            f.Shown += delegate { f.ActiveControl = ok; tb.SelectionStart = 0; tb.SelectionLength = 0; };
            f.ShowDialog(this);
        }

        void RunUpdate(UpdateInfo info)
        {
            if (updateBusy) return;
            updateBusy = true;
            AppendLog("Descargando Vortexs Live " + info.Tag + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string exe = Application.ExecutablePath;
                string err = null;
                try { InstallUpdate(info, exe); }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        updateBusy = false;
                        if (err != null)
                        {
                            AppendLog("No se pudo actualizar: " + err);
                            if (I18n.Show(this, "No se pudo actualizar:\n" + err + "\n\nTu versión actual sigue funcionando. ¿Abrir la página de descarga para hacerlo a mano?",
                                "Actualizar", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes) Http.OpenBrowser(info.Page);
                            return;
                        }
                        AppendLog("Actualización lista: reiniciando Vortexs Live…");
                        // La versión nueva espera a que esta se cierre del todo (así el Mando conserva su puerto).
                        Process.Start(new ProcessStartInfo(exe, "--esperar=" + Process.GetCurrentProcess().Id + " --actualizado=" + info.Tag.TrimStart('v', 'V'))
                        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                        Close();
                    });
                }
                catch { }
            });
        }

        static void InstallUpdate(UpdateInfo info, string exe)
        {
            string dir = Path.GetDirectoryName(exe);
            // ¿Se puede escribir en la carpeta del programa? (Si está en Archivos de programa, no.)
            string probe = Path.Combine(dir, ".actualizar-prueba");
            try { File.WriteAllText(probe, ""); File.Delete(probe); }
            catch { throw new Exception("no hay permiso para escribir en la carpeta de Vortexs Live (" + dir + "). Muévela a Documentos o al Escritorio."); }

            string tmp = Path.Combine(Path.GetTempPath(), "VortexsLive-actualizacion-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                string zip = Path.Combine(tmp, "VortexsLive.zip");
                Download(info.Url, zip);
                long len = new FileInfo(zip).Length;
                if (info.Size > 0 && len != info.Size) throw new Exception("la descarga está incompleta (" + len + " de " + info.Size + " bytes)");
                if (!string.IsNullOrEmpty(info.Sha256))
                {
                    string h;
                    using (var sha = SHA256.Create())
                    using (var s = File.OpenRead(zip))
                        h = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
                    if (h != info.Sha256) throw new Exception("la huella SHA-256 del ZIP no coincide con la de GitHub");
                }
                string ext = Path.Combine(tmp, "x");
                ZipFile.ExtractToDirectory(zip, ext);
                string newExe = Path.Combine(ext, "VortexsLive.exe");
                if (!File.Exists(newExe)) newExe = Path.Combine(ext, "StreamDelay.exe");
                if (!File.Exists(newExe)) throw new Exception("el ZIP no contiene VortexsLive.exe");
                using (var s = File.OpenRead(newExe))
                    if (s.ReadByte() != 'M' || s.ReadByte() != 'Z') throw new Exception("el VortexsLive.exe descargado no es válido");

                // Windows deja renombrar un programa abierto: el viejo pasa a .old y el nuevo ocupa su sitio.
                string old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try { File.Copy(newExe, exe); }
                catch { File.Move(old, exe); throw; }

                // Guía, LEEME, licencia… (nunca VortexsLive.ini).
                foreach (string f in Directory.GetFiles(ext, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(ext.Length + 1);
                    if (rel.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string dest = Path.Combine(dir, rel);
                    try { Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(f, dest, true); } catch { }
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        static void Download(string url, string path)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "VortexsLive";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            using (var resp = req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(path))
                src.CopyTo(dst);
        }

        // ---------- Configuración ----------

        void LoadUpdateConfig(Dictionary<string, string> raw)
        {
            string v;
            if (raw.TryGetValue("update_skip", out v)) updateSkip = v.Trim();
        }

        void SaveUpdateConfig(List<string> lines)
        {
            lines.Add("update_skip=" + updateSkip);
        }
    }
}
