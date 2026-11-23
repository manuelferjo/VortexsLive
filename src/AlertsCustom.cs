// Alertas a tu gusto (pestaña Overlays → Alertas → Personalizar): para cada tipo, un texto propio
// con {usuario} y {cantidad}, una imagen o GIF y un sonido; mínimo para bits y donaciones; y si se
// enseña el mensaje que escribe el espectador. Las imágenes se copian a la carpeta "alertas" junto a
// Vortexs Live (follow.gif, sub.png…) y los overlays las piden en /imagen/<tipo>.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// Texto propio de cada tipo (mismo orden que AlertKinds); vacío = el de siempre.
		private readonly string[] alertText = new string[6] { "", "", "", "", "", "" };

		// Mínimo para que salte: bits y donaciones (0 = todas).
		private int alertMinBits;

		private int alertMinTip;

		// Enseñar en la alerta el mensaje que escribe el espectador (bits, donaciones, Super Chats…).
		private bool alertShowMsg = true;

		private static readonly string[] ImageExts = new string[5] { ".gif", ".png", ".jpg", ".jpeg", ".webp" };

		private static readonly string[] AlertNames = new string[6] { "Follows", "Subs y regalos", "Raids", "Bits", "Canjes", "Donaciones" };

		private static string AlertImageDir
		{
			get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alertas"); }
		}

		private string AlertImageFile(string kind)
		{
			if (Array.IndexOf(AlertKinds, kind) < 0)
			{
				return null;
			}
			foreach (string ext in ImageExts)
			{
				string f = Path.Combine(AlertImageDir, kind + ext);
				if (File.Exists(f))
				{
					return f;
				}
			}
			return null;
		}

		// Lo que se añade a /widgets.json → alerts (lo llama WidgetsJson dentro de wgLock).
		private void AlertCustomJson(Dictionary<string, object> alerts)
		{
			Dictionary<string, object> texts = new Dictionary<string, object>();
			Dictionary<string, object> images = new Dictionary<string, object>();
			for (int i = 0; i < AlertKinds.Length; i++)
			{
				if (alertText[i].Length > 0)
				{
					texts[AlertKinds[i]] = alertText[i];
				}
				string img = AlertImageFile(AlertKinds[i]);
				if (img != null)
				{
					images[AlertKinds[i]] = "/imagen/" + AlertKinds[i] + "?v=" + File.GetLastWriteTimeUtc(img).Ticks;
				}
			}
			alerts["texts"] = texts;
			alerts["images"] = images;
			alerts["min"] = new Dictionary<string, object> { { "bits", alertMinBits }, { "tip", alertMinTip } };
			alerts["msg"] = alertShowMsg;
		}

		private void LoadAlertCustom(Dictionary<string, string> raw)
		{
			string v;
			int n;
			for (int i = 0; i < AlertKinds.Length; i++)
			{
				if (raw.TryGetValue("wg_alert_txt_" + AlertKinds[i], out v))
				{
					try
					{
						alertText[i] = Encoding.UTF8.GetString(Convert.FromBase64String(v));
					}
					catch
					{
					}
				}
			}
			if (raw.TryGetValue("wg_alert_min_bits", out v) && int.TryParse(v, out n) && n >= 0) alertMinBits = n;
			if (raw.TryGetValue("wg_alert_min_tip", out v) && int.TryParse(v, out n) && n >= 0) alertMinTip = n;
			if (raw.TryGetValue("wg_alert_msg", out v)) alertShowMsg = v != "0";
		}

		private void SaveAlertCustom(List<string> lines)
		{
			for (int i = 0; i < AlertKinds.Length; i++)
			{
				if (alertText[i].Length > 0)
				{
					lines.Add("wg_alert_txt_" + AlertKinds[i] + "=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(alertText[i])));
				}
			}
			lines.Add("wg_alert_min_bits=" + alertMinBits);
			lines.Add("wg_alert_min_tip=" + alertMinTip);
			lines.Add("wg_alert_msg=" + (alertShowMsg ? "1" : "0"));
		}

		// Copia un archivo elegido a carpeta\tipo.ext (borrando el de antes con otra extensión).
		private bool CopyAlertFile(Form owner, string src, string dir, string kind, string[] exts, long maxBytes)
		{
			try
			{
				FileInfo info = new FileInfo(src);
				string ext = info.Extension.ToLowerInvariant();
				if (Array.IndexOf(exts, ext) < 0)
				{
					return false;
				}
				if (info.Length > maxBytes)
				{
					Toast(I18n.Tr("Ese archivo pesa demasiado (máximo ") + maxBytes / (1024 * 1024) + " MB).", "warn");
					return false;
				}
				Directory.CreateDirectory(dir);
				DeleteAlertFile(dir, kind, exts);
				string dest = Path.Combine(dir, kind + ext);
				File.Copy(src, dest, true);
				File.SetLastWriteTimeUtc(dest, DateTime.UtcNow);
				return true;
			}
			catch (Exception ex)
			{
				Toast(I18n.Tr("No se pudo copiar el archivo: ") + ex.Message, "error");
				return false;
			}
		}

		private static void DeleteAlertFile(string dir, string kind, string[] exts)
		{
			foreach (string e in exts)
			{
				try
				{
					string old = Path.Combine(dir, kind + e);
					if (File.Exists(old))
					{
						File.Delete(old);
					}
				}
				catch
				{
				}
			}
		}

		// Ventana "Personalizar alertas": arriba el tipo; debajo, su texto, imagen, sonido y mínimo.
		private void AlertCustomDialog()
		{
			Form f = DarkForm("Personalizar alertas", 460, 640);
			Card c = new Card { Title = "Alertas a tu gusto", Subtitle = "Elige un tipo y cambia su texto, imagen, sonido o mínimo." };
			Place(f, c, 20f, 18f, 420f, 542f);
			int sel = 0;
			bool filling = false;
			VButton[] pills = new VButton[AlertKinds.Length];
			Field txt = new Field();
			Field min = new Field();
			PaintBox imgName = null;
			PaintBox sndName = null;
			PaintBox minHint = null;
			Action refresh = null;
			for (int i = 0; i < AlertKinds.Length; i++)
			{
				int idx = i;
				pills[i] = new VButton { Kind = ButtonKind.Pill, Text = AlertNames[i], Selected = i == 0, FontSize = 8f };
				Place(c, pills[i], 20f + (float)(i % 3) * 128f, 58f + (float)(i / 3) * 32f, 124f, 28f);
				pills[i].Click += delegate
				{
					sel = idx;
					refresh();
				};
			}
			// Texto.
			Label(c, "TEXTO", 20f, 128f, 380f);
			Place(c, txt, 20f, 146f, 380f, 34f);
			txt.Box.MaxLength = 90;
			Box(c, 20f, 182f, 380f, 30f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Vacío = el de siempre. Usa {usuario} y {cantidad}: «¡{usuario} ha donado {cantidad}!»", Theme.Body(8f), Theme.Muted, r);
			});
			txt.Box.TextChanged += delegate
			{
				if (filling)
				{
					return;
				}
				lock (wgLock)
				{
					alertText[sel] = txt.Box.Text.Replace("\r", " ").Replace("\n", " ").Trim();
				}
				SaveConfig();
			};
			// Imagen o GIF.
			Label(c, "IMAGEN O GIF (EN LUGAR DEL ICONO)", 20f, 218f, 380f);
			imgName = Box(c, 20f, 236f, 206f, 30f, delegate(Graphics g, Rectangle r)
			{
				string file = AlertImageFile(AlertKinds[sel]);
				Theme.DrawText(g, file == null ? "El icono de siempre" : Path.GetFileName(file), Theme.Body(8.5f), file == null ? Theme.Muted : Theme.Violet, r, StringAlignment.Near, StringAlignment.Center);
			});
			VButton imgPick = new VButton { Kind = ButtonKind.Ghost, Text = "Elegir…", FontSize = 8.5f };
			Place(c, imgPick, 232f, 236f, 126f, 30f);
			VButton imgClear = new VButton { Kind = ButtonKind.Ghost, Text = "✕", FontSize = 9f };
			Place(c, imgClear, 364f, 236f, 36f, 30f);
			imgPick.Click += delegate
			{
				using (OpenFileDialog dlg = new OpenFileDialog())
				{
					dlg.Title = I18n.Tr("Imagen para ") + I18n.Tr(AlertNames[sel]);
					dlg.Filter = I18n.Tr("Imágenes y GIF") + " (*.gif;*.png;*.jpg;*.webp)|*.gif;*.png;*.jpg;*.jpeg;*.webp";
					if (dlg.ShowDialog(f) == DialogResult.OK && CopyAlertFile(f, dlg.FileName, AlertImageDir, AlertKinds[sel], ImageExts, 10L * 1024 * 1024))
					{
						AppendLog("Alertas: imagen de " + AlertNames[sel] + " → " + Path.GetFileName(dlg.FileName));
					}
				}
				imgName.Invalidate();
			};
			imgClear.Click += delegate
			{
				DeleteAlertFile(AlertImageDir, AlertKinds[sel], ImageExts);
				imgName.Invalidate();
			};
			// Sonido.
			Label(c, "SONIDO", 20f, 274f, 380f);
			sndName = Box(c, 20f, 292f, 164f, 30f, delegate(Graphics g, Rectangle r)
			{
				string file = AlertSoundFile(AlertKinds[sel]);
				Theme.DrawText(g, file == null ? "Sonido de siempre" : Path.GetFileName(file), Theme.Body(8.5f), file == null ? Theme.Muted : Theme.Violet, r, StringAlignment.Near, StringAlignment.Center);
			});
			VButton sndPick = new VButton { Kind = ButtonKind.Ghost, Text = "Elegir…", FontSize = 8.5f };
			Place(c, sndPick, 190f, 292f, 126f, 30f);
			VButton sndPlay = new VButton { Kind = ButtonKind.Ghost, Text = "▶", FontSize = 9f };
			Place(c, sndPlay, 322f, 292f, 36f, 30f);
			VButton sndClear = new VButton { Kind = ButtonKind.Ghost, Text = "✕", FontSize = 9f };
			Place(c, sndClear, 364f, 292f, 36f, 30f);
			sndPick.Click += delegate
			{
				using (OpenFileDialog dlg = new OpenFileDialog())
				{
					dlg.Title = I18n.Tr("Sonido para ") + I18n.Tr(AlertNames[sel]);
					dlg.Filter = I18n.Tr("Sonidos") + " (*.mp3;*.wav;*.ogg;*.m4a)|*.mp3;*.wav;*.ogg;*.m4a";
					if (dlg.ShowDialog(f) == DialogResult.OK)
					{
						PlaySoundPreview(null, 0);
						if (CopyAlertFile(f, dlg.FileName, SoundDir, AlertKinds[sel], SoundExts, 5L * 1024 * 1024))
						{
							AppendLog("Alertas: sonido de " + AlertNames[sel] + " → " + Path.GetFileName(dlg.FileName));
						}
					}
				}
				sndName.Invalidate();
			};
			sndPlay.Click += delegate { PlaySoundPreview(AlertSoundFile(AlertKinds[sel]), alertVolume); };
			sndClear.Click += delegate
			{
				PlaySoundPreview(null, 0);
				DeleteAlertFile(SoundDir, AlertKinds[sel], SoundExts);
				sndName.Invalidate();
			};
			// Mínimo (solo bits y donaciones).
			Label(c, "MÍNIMO PARA QUE SALTE", 20f, 330f, 380f);
			Place(c, min, 20f, 348f, 110f, 34f);
			min.Box.MaxLength = 7;
			minHint = Box(c, 140f, 348f, 260f, 34f, delegate(Graphics g, Rectangle r)
			{
				string k = AlertKinds[sel];
				string h = (k == "bits") ? "bits (0 = todos)" : ((k == "tip") ? "en su moneda: 5 = 5 € (0 = todas)" : "solo para bits y donaciones");
				Theme.DrawText(g, h, Theme.Body(8.5f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});
			min.Box.TextChanged += delegate
			{
				int n;
				if (filling || !int.TryParse(min.Box.Text.Trim().Length == 0 ? "0" : min.Box.Text.Trim(), out n) || n < 0)
				{
					return;
				}
				lock (wgLock)
				{
					if (AlertKinds[sel] == "bits")
					{
						alertMinBits = n;
					}
					else if (AlertKinds[sel] == "tip")
					{
						alertMinTip = n;
					}
				}
				SaveConfig();
			};
			// Para todos los tipos.
			Toggle msg = new Toggle { Text = "Enseñar el mensaje del espectador", Hint = "Bits, donaciones y Super Chats con su texto", Checked = alertShowMsg };
			Place(c, msg, 18f, 394f, 384f, 40f);
			msg.CheckedChanged += delegate
			{
				lock (wgLock)
				{
					alertShowMsg = msg.Checked;
				}
				SaveConfig();
			};
			Label(c, "VOLUMEN EN OBS", 20f, 442f, 380f);
			int[] vols = new int[5] { 20, 40, 60, 80, 100 };
			int vk = 0;
			for (int i = 0; i < vols.Length; i++)
			{
				if (Math.Abs(vols[i] - alertVolume) < Math.Abs(vols[vk] - alertVolume))
				{
					vk = i;
				}
			}
			Choice(c, new string[5] { "20 %", "40 %", "60 %", "80 %", "100 %" }, vk, 20f, 460f, 70f, delegate(int i) { lock (wgLock) { alertVolume = vols[i]; } });
			Box(c, 20f, 494f, 380f, 36f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Para oír las alertas en el directo, en OBS marca «Controlar audio mediante OBS» en la fuente Navegador.", Theme.Body(8f), Theme.Muted, r);
			});
			refresh = delegate
			{
				filling = true;
				for (int i = 0; i < pills.Length; i++)
				{
					pills[i].Selected = i == sel;
					pills[i].Invalidate();
				}
				txt.Box.Text = alertText[sel];
				string k = AlertKinds[sel];
				bool hasMin = k == "bits" || k == "tip";
				min.Enabled = hasMin;
				min.Box.Text = hasMin ? ((k == "bits") ? alertMinBits : alertMinTip).ToString() : "";
				min.Invalidate();
				imgName.Invalidate();
				sndName.Invalidate();
				minHint.Invalidate();
				filling = false;
			};
			refresh();
			VButton test = new VButton { Kind = ButtonKind.Ghost, Text = "Probar una alerta", FontSize = 9.5f, Icon = "play" };
			VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "Listo", FontSize = 10f };
			Place(f, test, 20f, 574f, 190f, 44f);
			Place(f, ok, 218f, 574f, 222f, 44f);
			test.Click += delegate { SendTestAlert(); };
			ok.Click += delegate { f.Close(); };
			f.FormClosing += delegate { PlaySoundPreview(null, 0); SaveConfig(); };
			// En las capturas de GitHub se abre sin bloquear, para poder hacerle la foto.
			if (Program.CaptureDir != null)
			{
				f.Show(this);
				return;
			}
			f.ShowDialog(this);
		}
	}
}
