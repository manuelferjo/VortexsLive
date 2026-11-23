// Pestaña Clips (plan Completo): los clips guardados (verlos, abrir su carpeta, renombrarlos y
// mandarlos a la papelera) y quién puede pedir uno escribiendo !guardar en el chat.
// Lo de !guardar se guarda en VortexsLive.ini como antes: clip_mods=1 y clip_users=nombre1,nombre2
// (ver ChatClip en Clips.cs: los nombres solo valen en Twitch y Kick).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private ClipList clipList;

		private PaintBox clipPills;

		private Field fldClipUser;

		private Toggle tgClipMods;

		// Línea «CARPETA …» de la pestaña (se repinta al cambiar de carpeta).
		private PaintBox clipFolderBox;

		// Los nombres de clip_users, uno por elemento (sin @ y sin repetir).
		private List<string> ClipUserList()
		{
			List<string> list = new List<string>();
			foreach (string part in clipUsers.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				string name = part.Trim().TrimStart('@');
				if (name.Length > 0 && list.FindIndex((string x) => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) < 0)
				{
					list.Add(name);
				}
			}
			return list;
		}

		private void SetClipUsers(List<string> list)
		{
			clipUsers = string.Join(",", list.ToArray());
			SaveConfig();
		}

		private void BuildClips(int y0)
		{
			// --- Clips guardados ---
			Card c1 = new Card
			{
				Title = "Clips guardados",
				Subtitle = "Cada clip son los últimos " + ClipSeconds + " s que ha visto el público (con el delay)."
			};
			Place(this, c1, 20f, y0, 420f, 290f, PageClips);
			// Cómo se guarda uno: las cuatro formas, en pastillas.
			Box(c1, 20f, 58f, 380f, 26f, PaintClipWays);
			// Dónde se guardan, con «Cambiar» (abrir, elegir otra carpeta o volver a la de siempre).
			clipFolderBox = Box(c1, 20f, 90f, 300f, 22f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "CARPETA", Theme.Mono(7f), Theme.Muted, new RectangleF(r.X, r.Y, S(58f), r.Height), StringAlignment.Near, StringAlignment.Center);
				// La ruta no se traduce: se dibuja tal cual, con «…» en medio si es larga.
				using (StringFormat format = new StringFormat())
				{
					format.Trimming = StringTrimming.EllipsisPath;
					format.FormatFlags = StringFormatFlags.NoWrap;
					format.LineAlignment = StringAlignment.Center;
					using (SolidBrush brush = new SolidBrush(Theme.Text))
					{
						g.DrawString(ClipFolder, Theme.Body(8f), brush, new RectangleF(r.X + S(60f), r.Y, r.Width - S(60f), r.Height), format);
					}
				}
			});
			clipFolderBox.Cursor = Cursors.Hand;
			clipFolderBox.Click += delegate { OpenClipFolder(); };
			VButton change = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Cambiar",
				FontSize = 8.5f
			};
			Place(c1, change, 322f, 89f, 78f, 24f);
			change.Click += delegate { ShowClipMenu(change); };
			clipList = new ClipList();
			Place(c1, clipList, 16f, 120f, 388f, 116f);
			string[] names = new string[4] { "Ver", "Carpeta", "Renombrar", "Borrar" };
			VButton[] buttons = new VButton[4];
			for (int i = 0; i < 4; i++)
			{
				buttons[i] = new VButton
				{
					Kind = (i == 3) ? ButtonKind.Danger : ButtonKind.Ghost,
					Text = names[i],
					FontSize = 9f
				};
				Place(c1, buttons[i], 16f + (float)i * 99f, 242f, 91f, 34f);
			}
			buttons[0].Click += delegate { OpenClip(clipList.SelectedFile); };
			buttons[1].Click += delegate { ShowClipMenu(buttons[1]); };
			buttons[2].Click += delegate { RenameClip(clipList.SelectedFile); };
			buttons[3].Click += delegate { DeleteClip(clipList.SelectedFile); };
			clipList.Opened += delegate { OpenClip(clipList.SelectedFile); };
			clipList.DeleteRequested += delegate { DeleteClip(clipList.SelectedFile); };
			EventHandler sync = delegate
			{
				bool any = clipList.SelectedFile != null;
				buttons[0].Enabled = any;
				buttons[2].Enabled = any;
				buttons[3].Enabled = any;
			};
			clipList.SelectedChanged += sync;
			sync(null, EventArgs.Empty);

			// --- Quién puede usar !guardar ---
			int y2 = y0 + 298;
			Card c2 = new Card
			{
				Title = "Quién puede usar " + ClipCommand
			};
			Place(this, c2, 20f, y2, 420f, 210f, PageClips);
			tgClipMods = new Toggle
			{
				Text = "También los moderadores",
				Checked = clipMods
			};
			Place(c2, tgClipMods, 20f, 44f, 380f, 30f);
			tgClipMods.CheckedChanged += delegate
			{
				clipMods = tgClipMods.Checked;
				SaveConfig();
				RebuildClipPills();
			};
			Label(c2, "AÑADIR A ALGUIEN (NOMBRE DE TWITCH O KICK)", 20f, 78f, 380f);
			fldClipUser = new Field();
			Place(c2, fldClipUser, 20f, 96f, 290f, 34f);
			VButton add = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Añadir",
				FontSize = 9f
			};
			Place(c2, add, 318f, 96f, 82f, 34f);
			Action addUser = delegate
			{
				// Se pueden pegar varios a la vez, separados por comas o espacios.
				List<string> users = ClipUserList();
				foreach (string part in fldClipUser.Box.Text.Split(new char[3] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
				{
					string name = part.Trim().TrimStart('@');
					if (name.Length > 0 && users.FindIndex((string x) => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) < 0)
					{
						users.Add(name);
						AppendLog(name + " ya puede usar " + ClipCommand + ".");
					}
				}
				SetClipUsers(users);
				fldClipUser.Box.Text = "";
				RebuildClipPills();
			};
			add.Click += delegate { addUser(); };
			fldClipUser.Box.KeyDown += delegate(object s, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					addUser();
				}
			};
			clipPills = Box(c2, 20f, 138f, 380f, 62f, delegate { });
			RebuildClipPills();
		}

		// Las pastillas de quién puede usar !guardar: tú siempre, los moderadores si están activados
		// y cada nombre añadido (con ✕ para quitarlo). Caben dos filas; el resto sale como «+N».
		private void RebuildClipPills()
		{
			if (clipPills == null)
			{
				return;
			}
			clipPills.SuspendLayout();
			Control[] olds = new Control[clipPills.Controls.Count];
			clipPills.Controls.CopyTo(olds, 0);
			foreach (Control old in olds)
			{
				clipPills.Controls.Remove(old);
				old.Dispose();
			}
			List<KeyValuePair<string, string>> pills = new List<KeyValuePair<string, string>>();
			pills.Add(new KeyValuePair<string, string>("Tú (dueño del canal)", null));
			if (clipMods)
			{
				pills.Add(new KeyValuePair<string, string>("Moderadores", null));
			}
			foreach (string user in ClipUserList())
			{
				pills.Add(new KeyValuePair<string, string>(user + "  ✕", user));
			}
			Font font = Theme.Mono(7.5f);
			float x = 0f;
			float y = 0f;
			float rowH = 32f;
			float maxW = 380f;
			for (int i = 0; i < pills.Count; i++)
			{
				string label = pills[i].Key;
				string user = pills[i].Value;
				float w = Math.Min(maxW, (float)TextRenderer.MeasureText(I18n.Tr(label), font).Width / Theme.Scale + 26f);
				// Si en la segunda fila no cabe este y queda alguno más, «+N» en su lugar.
				bool lastRow = y > 0f;
				float moreW = 46f;
				if (x + w > maxW || (lastRow && i < pills.Count - 1 && x + w + 6f + moreW > maxW))
				{
					if (lastRow)
					{
						VButton more = new VButton
						{
							Kind = ButtonKind.Pill,
							Text = "+" + (pills.Count - i),
							FontSize = 7.5f,
							Enabled = false
						};
						Place(clipPills, more, x, y, moreW, 26f);
						break;
					}
					x = 0f;
					y += rowH;
				}
				VButton p = new VButton
				{
					Kind = ButtonKind.Pill,
					Text = label,
					FontSize = 7.5f,
					Selected = user == null
				};
				if (user == null)
				{
					p.Cursor = Cursors.Default;
					p.TabStop = false;
				}
				else
				{
					p.Click += delegate
					{
						List<string> users = ClipUserList();
						users.Remove(user);
						SetClipUsers(users);
						AppendLog(user + " ya no puede usar " + ClipCommand + ".");
						BeginInvoke((Action)RebuildClipPills);
					};
				}
				Place(clipPills, p, x, y, w, 26f);
				x += w + 6f;
			}
			clipPills.ResumeLayout();
			clipPills.Invalidate();
		}

		// Las formas de guardar un clip, en pastillas: atajo, botón de Directo, comando del chat y
		// el Mando / Stream Deck. Si no caben, la letra se hace un poco más pequeña.
		private void PaintClipWays(Graphics g, Rectangle r)
		{
			Theme.Quality(g);
			string[] ways = new string[4] { "Ctrl+Alt+C", "Botón Clip", ClipCommand + " en el chat", "Mando · Stream Deck" };
			float gap = S(6f);
			float size = 7.5f;
			float[] w = new float[ways.Length];
			for (int pass = 0; pass < 3; pass++)
			{
				float total = gap * (float)(ways.Length - 1);
				for (int i = 0; i < ways.Length; i++)
				{
					w[i] = (float)TextRenderer.MeasureText(I18n.Tr(ways[i]), Theme.Mono(size)).Width + S(14f);
					total += w[i];
				}
				if (total <= (float)r.Width)
				{
					break;
				}
				size -= 0.5f;
			}
			float x = r.X;
			for (int i = 0; i < ways.Length; i++)
			{
				RectangleF chip = new RectangleF(x, (float)r.Y + 0.5f, Math.Min(w[i], (float)r.Right - x - 1f), (float)r.Height - 1.5f);
				using (System.Drawing.Drawing2D.GraphicsPath path = Theme.Round(chip, chip.Height / 2f))
				{
					using (SolidBrush brush = new SolidBrush(Theme.Input))
					{
						g.FillPath(brush, path);
					}
					using (Pen pen = new Pen(Theme.Border))
					{
						g.DrawPath(pen, path);
					}
				}
				Theme.DrawText(g, ways[i], Theme.Mono(size), Theme.Text, chip, StringAlignment.Center, StringAlignment.Center);
				x += w[i] + gap;
			}
		}

		// Vuelve a leer la carpeta de clips (al abrir la pestaña y al guardar uno).
		private void RefreshClipList()
		{
			if (clipFolderBox != null)
			{
				clipFolderBox.Invalidate();
			}
			if (clipList == null)
			{
				return;
			}
			List<FileInfo> files = new List<FileInfo>();
			try
			{
				DirectoryInfo dir = new DirectoryInfo(ClipFolder);
				if (dir.Exists)
				{
					foreach (FileInfo f in dir.GetFiles())
					{
						string ext = f.Extension.ToLowerInvariant();
						if ((ext == ".mp4" || ext == ".flv") && !clipsToDelete.Contains(f.FullName))
						{
							files.Add(f);
						}
					}
				}
			}
			catch
			{
			}
			files.Sort((FileInfo a, FileInfo b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
			clipList.SetFiles(files);
		}

		private void OpenClip(string path)
		{
			if (path == null)
			{
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo(path)
				{
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				AppendLog("No se pudo abrir el clip (" + ex.Message + ").");
			}
		}

		private void RenameClip(string path)
		{
			if (path == null)
			{
				return;
			}
			string ext = Path.GetExtension(path);
			using (Form f = DarkForm("Renombrar clip", 460, 176))
			{
				Label(f, "NOMBRE NUEVO", 20f, 16f, 300f);
				Field name = new Field();
				Place(f, name, 20f, 36f, 420f, 34f);
				name.Box.Text = Path.GetFileNameWithoutExtension(path);
				VButton cancel = new VButton
				{
					Kind = ButtonKind.Ghost,
					Text = "Cancelar"
				};
				Place(f, cancel, 20f, 116f, 204f, 40f);
				VButton ok = new VButton
				{
					Kind = ButtonKind.Primary,
					Text = "Guardar"
				};
				Place(f, ok, 236f, 116f, 204f, 40f);
				PaintBox error = Box(f, 20f, 78f, 420f, 30f, delegate { });
				string problem = "";
				error.Painter = delegate(Graphics g, Rectangle r)
				{
					Theme.DrawText(g, problem, Theme.Body(8f), Theme.Red, r, StringAlignment.Near, StringAlignment.Center);
				};
				cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; };
				Action accept = delegate
				{
					string wanted = name.Box.Text.Trim();
					if (wanted.Length == 0 || wanted.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
					{
						problem = "Escribe un nombre sin \\ / : * ? \" < > |";
						error.Invalidate();
						return;
					}
					string target = Path.Combine(Path.GetDirectoryName(path), wanted + ext);
					if (target == path)
					{
						f.DialogResult = DialogResult.OK;
						return;
					}
					if (File.Exists(target) && !string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
					{
						problem = "Ya hay un clip con ese nombre.";
						error.Invalidate();
						return;
					}
					try
					{
						File.Move(path, target);
						f.DialogResult = DialogResult.OK;
						RefreshClipList();
						clipList.SelectFile(target);
					}
					catch (Exception ex)
					{
						problem = "No se pudo renombrar: " + ex.Message;
						error.Invalidate();
					}
				};
				ok.Click += delegate { accept(); };
				name.Box.KeyDown += delegate(object s, KeyEventArgs e)
				{
					if (e.KeyCode == Keys.Enter)
					{
						e.SuppressKeyPress = true;
						accept();
					}
					else if (e.KeyCode == Keys.Escape)
					{
						e.SuppressKeyPress = true;
						f.DialogResult = DialogResult.Cancel;
					}
				};
				f.Shown += delegate
				{
					name.Box.Focus();
					name.Box.SelectAll();
				};
				f.ShowDialog(this);
			}
		}

		// Clips que se van a mandar a la papelera cuando acabe el aviso con "Deshacer".
		private readonly List<string> clipsToDelete = new List<string>();

		// A la papelera de reciclaje (se puede recuperar). Sin preguntar: desaparece de la lista y sale
		// un aviso con "Deshacer"; si no se pulsa en unos segundos, se manda a la papelera de verdad.
		private void DeleteClip(string path)
		{
			if (path == null || clipsToDelete.Contains(path))
			{
				return;
			}
			clipsToDelete.Add(path);
			RefreshClipList();
			Toast(I18n.Tr("Clip a la papelera: ") + Path.GetFileName(path), "info", "Deshacer", delegate
			{
				clipsToDelete.Remove(path);
				RefreshClipList();
			});
			System.Windows.Forms.Timer later = new System.Windows.Forms.Timer
			{
				Interval = 8000
			};
			later.Tick += delegate
			{
				later.Stop();
				later.Dispose();
				FlushClipDelete(path);
			};
			later.Start();
		}

		private void FlushClipDelete(string path)
		{
			if (!clipsToDelete.Remove(path))
			{
				return;
			}
			try
			{
				FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
				AppendLog("Clip enviado a la papelera: " + Path.GetFileName(path));
			}
			catch (Exception ex)
			{
				AppendLog("No se pudo borrar el clip (" + ex.Message + ").");
			}
			RefreshClipList();
		}

		// Al cerrar el programa, lo que estaba esperando su "Deshacer" se manda ya a la papelera.
		private void FlushAllClipDeletes()
		{
			foreach (string path in clipsToDelete.ToArray())
			{
				FlushClipDelete(path);
			}
		}
	}

	// Lista de clips dibujada a mano: nombre, fecha y tamaño; clic para elegir, doble clic o Intro
	// para verlo, Supr para borrarlo, rueda o flechas para moverse.
	internal class ClipList : VControl
	{
		private List<FileInfo> files = new List<FileInfo>();

		private int sel = -1;

		private int hot = -1;

		private int top;

		public event EventHandler SelectedChanged;

		public event EventHandler Opened;

		public event EventHandler DeleteRequested;

		private static float RowH
		{
			get
			{
				return Theme.S(37f);
			}
		}

		private int RowsShown
		{
			get
			{
				return Math.Max(1, (int)(((float)base.Height - (float)Theme.S(4f)) / RowH));
			}
		}

		public string SelectedFile
		{
			get
			{
				if (sel < 0 || sel >= files.Count)
				{
					return null;
				}
				return files[sel].FullName;
			}
		}

		public ClipList()
		{
			Cursor = Cursors.Hand;
			base.TabStop = true;
		}

		// Cambia los archivos y mantiene elegido el mismo clip si sigue ahí.
		public void SetFiles(List<FileInfo> list)
		{
			string was = SelectedFile;
			files = list;
			int idx = -1;
			if (was != null)
			{
				idx = files.FindIndex((FileInfo f) => string.Equals(f.FullName, was, StringComparison.OrdinalIgnoreCase));
			}
			SetSel(idx, true);
			ClampTop();
			Invalidate();
		}

		public void SelectFile(string path)
		{
			SetSel(files.FindIndex((FileInfo f) => string.Equals(f.FullName, path, StringComparison.OrdinalIgnoreCase)), true);
		}

		private void SetSel(int i, bool always)
		{
			if (i >= files.Count)
			{
				i = files.Count - 1;
			}
			if (i == sel && !always)
			{
				return;
			}
			bool changed = i != sel;
			sel = i;
			if (sel >= 0)
			{
				if (sel < top)
				{
					top = sel;
				}
				else if (sel >= top + RowsShown)
				{
					top = sel - RowsShown + 1;
				}
			}
			Invalidate();
			if ((changed || always) && this.SelectedChanged != null)
			{
				this.SelectedChanged(this, EventArgs.Empty);
			}
		}

		private void ClampTop()
		{
			top = Math.Max(0, Math.Min(top, files.Count - RowsShown));
		}

		private int At(int y)
		{
			int i = top + (int)(((float)y - (float)Theme.S(2f)) / RowH);
			if (y < Theme.S(2f) || i < 0 || i >= files.Count)
			{
				return -1;
			}
			return i;
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			int i = At(e.Y);
			if (i != hot)
			{
				hot = i;
				Invalidate();
			}
			base.OnMouseMove(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			hot = -1;
			base.OnMouseLeave(e);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			Focus();
			int i = At(e.Y);
			if (i >= 0)
			{
				SetSel(i, false);
			}
			base.OnMouseDown(e);
		}

		protected override void OnMouseDoubleClick(MouseEventArgs e)
		{
			if (At(e.Y) >= 0 && this.Opened != null)
			{
				this.Opened(this, EventArgs.Empty);
			}
			base.OnMouseDoubleClick(e);
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			top -= Math.Sign(e.Delta);
			ClampTop();
			hot = At(e.Y);
			Invalidate();
			base.OnMouseWheel(e);
		}

		protected override bool IsInputKey(Keys keyData)
		{
			if (keyData == Keys.Up || keyData == Keys.Down)
			{
				return true;
			}
			return base.IsInputKey(keyData);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Down)
			{
				SetSel(Math.Min(files.Count - 1, sel + 1), false);
			}
			else if (e.KeyCode == Keys.Up)
			{
				SetSel(Math.Max((files.Count > 0) ? 0 : (-1), sel - 1), false);
			}
			else if (e.KeyCode == Keys.Enter && sel >= 0 && this.Opened != null)
			{
				this.Opened(this, EventArgs.Empty);
			}
			else if (e.KeyCode == Keys.Delete && sel >= 0 && this.DeleteRequested != null)
			{
				this.DeleteRequested(this, EventArgs.Empty);
			}
			base.OnKeyDown(e);
		}

		private static string FormatSize(long bytes)
		{
			if (bytes >= 1048576)
			{
				return ((double)bytes / 1048576.0).ToString("0.0") + " MB";
			}
			return Math.Max(1L, bytes / 1024) + " KB";
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			Theme.Quality(g);
			RectangleF frame = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(frame, Theme.S(12f)))
			{
				using (SolidBrush brush = new SolidBrush(Theme.Input))
				{
					g.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Focused ? Color.FromArgb(150, Theme.Violet) : Theme.Border))
				{
					g.DrawPath(pen, path);
				}
				g.SetClip(path);
			}
			if (files.Count == 0)
			{
				RectangleF r = new RectangleF(Theme.S(20f), 0f, (float)base.Width - (float)Theme.S(40f), base.Height);
				Theme.DrawText(g, "Aún no hay clips.", Theme.BodySemi(9f), Theme.Text, new RectangleF(r.X, r.Y, r.Width, r.Height / 2f - (float)Theme.S(2f)), StringAlignment.Center, StringAlignment.Far);
				Theme.DrawText(g, "Botón Clip en Directo, Ctrl+Alt+C o !guardar en el chat.", Theme.Body(8f), Theme.Muted, new RectangleF(r.X, r.Height / 2f + (float)Theme.S(2f), r.Width, (float)Theme.S(18f)), StringAlignment.Center, StringAlignment.Near);
				g.ResetClip();
				base.OnPaint(e);
				return;
			}
			bool scroll = files.Count > RowsShown;
			float right = (float)base.Width - (float)Theme.S(scroll ? 14f : 6f);
			float rowH = RowH;
			Font nameFont = Theme.BodySemi(9f);
			Font infoFont = Theme.Mono(7f);
			for (int i = top; i < files.Count && i < top + RowsShown + 1; i++)
			{
				RectangleF row = new RectangleF(Theme.S(5f), (float)Theme.S(2f) + (float)(i - top) * rowH + (float)Theme.S(2f), right - (float)Theme.S(5f), rowH - (float)Theme.S(4f));
				if (i == sel || i == hot)
				{
					using (GraphicsPath path2 = Theme.Round(row, Theme.S(9f)))
					{
						using (SolidBrush brush2 = new SolidBrush((i == sel) ? Color.FromArgb(40, Theme.Violet) : Theme.Elevated))
						{
							g.FillPath(brush2, path2);
						}
						if (i == sel)
						{
							using (Pen pen2 = new Pen(Color.FromArgb(120, Theme.Violet)))
							{
								g.DrawPath(pen2, path2);
							}
						}
					}
				}
				// Botón de play: círculo con triángulo.
				float d = Theme.S(22f);
				RectangleF dot = new RectangleF(row.X + (float)Theme.S(7f), row.Y + (row.Height - d) / 2f, d, d);
				using (SolidBrush brush3 = new SolidBrush(Color.FromArgb((i == sel) ? 90 : 50, Theme.Violet)))
				{
					g.FillEllipse(brush3, dot);
				}
				float cx = dot.X + d / 2f + (float)Theme.S(1f);
				float cy = dot.Y + d / 2f;
				float t = Theme.S(4.5f);
				using (SolidBrush brush4 = new SolidBrush(Theme.VioletPale))
				{
					g.FillPolygon(brush4, new PointF[3]
					{
						new PointF(cx - t * 0.8f, cy - t),
						new PointF(cx - t * 0.8f, cy + t),
						new PointF(cx + t, cy)
					});
				}
				FileInfo f = files[i];
				float tx = dot.Right + (float)Theme.S(9f);
				RectangleF nameRect = new RectangleF(tx, row.Y + (float)Theme.S(2f), row.Right - tx - (float)Theme.S(8f), row.Height / 2f);
				RectangleF infoRect = new RectangleF(tx, row.Y + row.Height / 2f - (float)Theme.S(1f), nameRect.Width, row.Height / 2f - (float)Theme.S(1f));
				// El nombre no se traduce (es el del archivo): se dibuja directamente.
				using (StringFormat format = new StringFormat())
				{
					format.Trimming = StringTrimming.EllipsisCharacter;
					format.FormatFlags = StringFormatFlags.NoWrap;
					format.LineAlignment = StringAlignment.Center;
					using (SolidBrush brush5 = new SolidBrush((i == sel) ? Theme.VioletPale : Theme.Text))
					{
						g.DrawString(Path.GetFileNameWithoutExtension(f.Name), nameFont, brush5, nameRect, format);
					}
					string info = f.LastWriteTime.ToString("dd/MM/yyyy  HH:mm") + "  ·  " + FormatSize(f.Length) + "  ·  " + f.Extension.TrimStart('.').ToUpperInvariant();
					using (SolidBrush brush6 = new SolidBrush(Theme.Muted))
					{
						g.DrawString(info, infoFont, brush6, infoRect, format);
					}
				}
			}
			g.ResetClip();
			if (scroll)
			{
				float trackTop = Theme.S(8f);
				float trackH = (float)base.Height - (float)Theme.S(16f);
				float thumbH = Math.Max(Theme.S(18f), trackH * (float)RowsShown / (float)files.Count);
				float thumbY = trackTop + (trackH - thumbH) * (float)top / (float)Math.Max(1, files.Count - RowsShown);
				using (GraphicsPath path3 = Theme.Round(new RectangleF((float)base.Width - (float)Theme.S(10f), thumbY, Theme.S(4f), thumbH), Theme.S(2f)))
				{
					using (SolidBrush brush7 = new SolidBrush(Theme.BorderHi))
					{
						g.FillPath(brush7, path3);
					}
				}
			}
			base.OnPaint(e);
		}
	}
}
