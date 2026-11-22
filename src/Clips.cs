// Clips: guarda los últimos 60 s que ha visto el público (con el delay ya aplicado) en
// Vídeos\Vortexs Live o en la carpeta que elija el usuario (botón Clips de Registro).
// Se pide con el botón Clip de Directo, Ctrl+Alt+C, el Mando, el Stream Deck
// o !guardar en el chat (el dueño del canal y quien se elija en la pestaña Clips). Si hay ffmpeg en el PC, el clip se pasa a MP4
// (sin volver a codificar); si no, se queda en FLV (se abre con VLC).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private const int ClipSeconds = 60;

		// Comando del chat para guardar el clip (solo el dueño del canal).
		private const string ClipCommand = "!guardar";

		// Entre dos clips tiene que pasar al menos esto (por si se pulsa varias veces seguidas).
		private const int ClipCooldownSec = 15;

		private DateTime lastClipAt = DateTime.MinValue;

		private volatile bool clipBusy;

		// Carpeta elegida por el usuario (vacía = la de siempre). Se guarda en VortexsLive.ini (clip_dir).
		private static string clipDirCustom = "";

		// Quién más puede usar !guardar (además del dueño del canal): los moderadores (clip_mods) y
		// una lista de nombres de Twitch o Kick (clip_users), p. ej. el editor, desde cualquier sitio.
		private bool clipMods;

		private string clipUsers = "";

		private static string ClipFolder
		{
			get
			{
				if (clipDirCustom.Length > 0)
				{
					return clipDirCustom;
				}
				// Quien venía de StreamDelay sigue guardando en su carpeta de siempre.
				string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
				string old = Path.Combine(videos, "StreamDelay");
				return Directory.Exists(old) ? old : Path.Combine(videos, "Vortexs Live");
			}
		}

		// Guarda un clip en otro hilo. who: quién lo pidió (para el registro). done(ok, mensaje) se
		// llama desde ese hilo al terminar (o enseguida si no se puede); puede ser null.
		// Devuelve false si no se empieza (ya se está guardando o se acaba de guardar uno).
		private bool SaveClip(string who, Action<bool, string> done)
		{
			return SaveClip(who, done, null, null);
		}

		// plat y user: plataforma y usuario de quien escribió !guardar (null si fue el atajo, el botón
		// o el Mando). Al guardarse sale un aviso en el chat conjunto.
		private bool SaveClip(string who, Action<bool, string> done, string plat, string user)
		{
			// Los clips son del plan Completo.
			if (!ClipsAllowed())
			{
				AppendLog("Clips: son del plan Completo. Botón del plan, arriba.");
				if (done != null)
				{
					done(false, "Los clips (últimos 60 s) son del plan Completo.");
				}
				return false;
			}
			lock (this)
			{
				if (clipBusy || (DateTime.UtcNow - lastClipAt).TotalSeconds < ClipCooldownSec)
				{
					if (done != null)
					{
						done(false, "Se acaba de guardar un clip: espera unos segundos.");
					}
					return false;
				}
				clipBusy = true;
				lastClipAt = DateTime.UtcNow;
			}
			int mainTrack = engine.MainTrack;
			ThreadPool.QueueUserWorkItem(delegate
			{
				bool ok = false;
				string message;
				try
				{
					Directory.CreateDirectory(ClipFolder);
					string flv = Path.Combine(ClipFolder, "Clip " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".flv");
					double seconds = engine.Clips.Save(flv, ClipSeconds, mainTrack);
					string file = ToMp4(flv) ?? flv;
					ok = true;
					message = "Clip de " + Math.Round(seconds) + " s guardado: " + file;
					SummaryClip(who);
					ClipNotice((int)Math.Round(seconds), who, plat, user);
				}
				catch (Exception ex)
				{
					message = "No se pudo guardar el clip: " + ex.Message + ".";
				}
				clipBusy = false;
				LogLater(message + " (" + who + ")");
				// La pestaña Clips enseña el nuevo.
				try
				{
					BeginInvoke((Action)RefreshClipList);
				}
				catch
				{
				}
				if (done != null)
				{
					done(ok, message);
				}
			});
			return true;
		}

		// Pasa el FLV a MP4 con ffmpeg (-c copy, tarda un segundo). null si no hay ffmpeg o falla
		// (entonces se queda el FLV).
		private static string ToMp4(string flv)
		{
			string mp4 = Path.ChangeExtension(flv, ".mp4");
			foreach (string ffmpeg in Engine.FfmpegCandidates())
			{
				try
				{
					ProcessStartInfo startInfo = new ProcessStartInfo(ffmpeg, "-hide_banner -loglevel error -y -i \"" + flv + "\" -c copy -movflags +faststart \"" + mp4 + "\"")
					{
						UseShellExecute = false,
						CreateNoWindow = true
					};
					using (Process process = Process.Start(startInfo))
					{
						if (!process.WaitForExit(60000))
						{
							try
							{
								process.Kill();
							}
							catch
							{
							}
							continue;
						}
						if (process.ExitCode == 0 && File.Exists(mp4) && new FileInfo(mp4).Length > 0)
						{
							File.Delete(flv);
							return mp4;
						}
					}
				}
				catch
				{
				}
			}
			try
			{
				if (File.Exists(mp4))
				{
					File.Delete(mp4);
				}
			}
			catch
			{
			}
			return null;
		}

		// Menú del botón Carpeta (pestaña Clips): abrir la carpeta, elegir otra o volver a la de siempre.
		private void ShowClipMenu(Control anchor)
		{
			ContextMenuStrip menu = DarkMenu();
			ToolStripMenuItem open = new ToolStripMenuItem(I18n.Tr("Abrir la carpeta de clips"));
			open.Click += delegate { OpenClipFolder(); };
			menu.Items.Add(open);
			ToolStripMenuItem choose = new ToolStripMenuItem(I18n.Tr("Elegir dónde se guardan los clips…"));
			choose.Click += delegate { ChooseClipFolder(); };
			menu.Items.Add(choose);
			if (clipDirCustom.Length > 0)
			{
				ToolStripMenuItem reset = new ToolStripMenuItem(I18n.Tr("Volver a la carpeta de siempre"));
				reset.Click += delegate
				{
					clipDirCustom = "";
					SaveConfig();
					AppendLog("Los clips se guardan en " + ClipFolder + ".");
					RefreshClipList();
				};
				menu.Items.Add(reset);
			}
			ToolStripMenuItem where = new ToolStripMenuItem(ClipFolder)
			{
				Enabled = false
			};
			menu.Items.Add(new ToolStripSeparator());
			menu.Items.Add(where);
			menu.Show(anchor, new Point(0, anchor.Height));
		}

		private void ChooseClipFolder()
		{
			using (FolderBrowserDialog dlg = new FolderBrowserDialog())
			{
				dlg.Description = I18n.Tr("Carpeta donde se guardan los clips (últimos 60 s)");
				dlg.ShowNewFolderButton = true;
				try
				{
					Directory.CreateDirectory(ClipFolder);
					dlg.SelectedPath = ClipFolder;
				}
				catch
				{
				}
				if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(dlg.SelectedPath))
				{
					return;
				}
				clipDirCustom = dlg.SelectedPath;
				SaveConfig();
				AppendLog("Los clips se guardan en " + ClipFolder + ".");
				RefreshClipList();
			}
		}

		private void OpenClipFolder()
		{
			try
			{
				Directory.CreateDirectory(ClipFolder);
				Process.Start(new ProcessStartInfo(ClipFolder)
				{
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				AppendLog("No se pudo abrir la carpeta de clips (" + ex.Message + ").");
			}
		}

		// !guardar en el chat: el dueño del canal y, si él quiere, sus moderadores y las personas de su
		// lista (el editor, aunque esté en otra casa). El resto de espectadores no puede, para que nadie
		// llene el PC de vídeos. No contesta en el chat: queda en el Registro.
		// La lista solo vale en Twitch y Kick, donde el nombre es único (en YouTube y TikTok cualquiera
		// podría ponerse el mismo nombre visible).
		// (!clip no es de Vortexs Live: es para el bot que hace clips de Twitch, como StreamElements.)
		// Aviso en el chat conjunto (y en el chat en pantalla) de que se ha guardado un clip.
		private void ClipNotice(int seconds, string who, string plat, string user)
		{
			try
			{
				if (chatServer == null || !chatServer.Running)
				{
					return;
				}
				bool byUser = !string.IsNullOrEmpty(user);
				chatServer.Send(new ChatItem
				{
					t = "ev",
					p = byUser ? plat : "local",
					kind = "clip",
					user = byUser ? user : "",
					text = byUser ? (user + " ha guardado un clip de " + seconds + " s") : ("Clip de " + seconds + " s guardado · " + who)
				});
			}
			catch
			{
			}
		}

		private void ChatClip(ChatItem it, string plat)
		{
			bool owner = it.roles != null && Array.IndexOf(it.roles, "broadcaster") >= 0;
			bool mod = clipMods && it.roles != null && Array.IndexOf(it.roles, "mod") >= 0;
			bool listed = (it.p == "twitch" || it.p == "kick") && ClipUserAllowed(it.user);
			if (!owner && !mod && !listed)
			{
				return;
			}
			SaveClip(ClipCommand + " en " + plat + (owner ? "" : (" (" + it.user + ")")), null, it.p, it.user);
		}

		private bool ClipUserAllowed(string user)
		{
			if (string.IsNullOrEmpty(user) || clipUsers.Length == 0)
			{
				return false;
			}
			string u = user.Trim().TrimStart('@');
			foreach (string part in clipUsers.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (string.Equals(part.Trim().TrimStart('@'), u, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}
	}
}
