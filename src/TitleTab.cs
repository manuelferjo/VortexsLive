// Pestaña Título: cambiar el título y la categoría en Twitch, YouTube y Kick a la vez
// (TikTok no lo permite: se copia el título para pegarlo en LIVE Studio).
// También están aquí las ventanas para crear la app de cada plataforma y conectarla.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// Lo que la ventana "Conectar" enseña mientras la plataforma inicia sesión
		// (código para escribir en la web, o aviso de que se abrió el navegador).
		private class LoginUi : ILoginUi
		{
			public volatile bool Cancel;

			public Action<string, string> OnCode;

			public Action<string> OnBrowser;

			public bool Cancelled
			{
				get
				{
					return Cancel;
				}
			}

			public void ShowCode(string code, string url)
			{
				OnCode(code, url);
			}

			public void ShowBrowserWait(string url)
			{
				OnBrowser(url);
			}
		}

		// ---------- Construcción de la pestaña ----------

		private void BuildTitle(int y0)
		{
			plats = new Platform[3] { twitch, youtube, kick };

			// Recuadro de arriba: título, categoría y los 4 "chips" con la categoría elegida en cada plataforma.
			Card titleCard = new Card
			{
				Title = "Título y categoría"
			};
			Place(this, titleCard, 20f, y0, 420f, 236f, PageTitle);
			// Los avisos del delay en el chat usan estas cuentas: son del plan Básico.
			VButton annLink = new VButton { Kind = ButtonKind.Link, Text = "Avisos del delay →", FontSize = 8.5f };
			Place(titleCard, annLink, 230f, 13f, 170f, 24f);
			annLink.Click += delegate { AnnounceDialog(); };
			Label(titleCard, "TÍTULO", 20f, 44f, 120f);
			btnRecent = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Recientes",
				FontSize = 8f
			};
			Place(titleCard, btnRecent, 196f, 39f, 110f, 22f);
			btnRecent.Click += delegate
			{
				ShowRecent();
			};
			// Contador de caracteres (en rojo si pasa del máximo de alguna plataforma conectada).
			titleCounter = Box(titleCard, 310f, 44f, 90f, 18f, delegate(Graphics g, Rectangle r)
			{
				int length = fldTitle.Box.Text.Length;
				int max = TitleMax();
				Theme.DrawText(g, length + "/" + max, Theme.Mono(7f), (length > max) ? Theme.Red : Theme.Dim, r, StringAlignment.Far, StringAlignment.Center);
			});
			fldTitle = new Field
			{
				ColorEmoji = true
			};
			Place(titleCard, fldTitle, 20f, 62f, 330f, 36f);
			fldTitle.Box.MaxLength = 200;
			// 😊: abre el panel de emojis de Windows (el mismo de Win + .), que los pone en el título.
			VButton emojiBtn = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "😊",
				FontSize = 11f
			};
			Place(titleCard, emojiBtn, 356f, 62f, 44f, 36f);
			emojiBtn.Click += delegate
			{
				OpenEmojiPanel(fldTitle);
			};
			fldTitle.Box.TextChanged += delegate
			{
				titleCounter.Invalidate();
				if (!loading)
				{
					SaveConfig();
				}
			};
			fldTitle.Box.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					UpdateTitles();
				}
			};
			Label(titleCard, "CATEGORÍA O JUEGO", 20f, 106f, 200f);
			fldCat = new Field();
			Place(titleCard, fldCat, 20f, 124f, 292f, 36f);
			fldCat.Box.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.SuppressKeyPress = true;
					SearchCategory();
				}
			};
			btnSearch = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Buscar",
				FontSize = 9f
			};
			Place(titleCard, btnSearch, 320f, 124f, 80f, 36f);
			btnSearch.Click += delegate
			{
				SearchCategory();
			};
			for (int i = 0; i < 4; i++)
			{
				int idx = i;
				chips[i] = new VButton
				{
					Kind = ButtonKind.Pill,
					FontSize = 7.25f
				};
				Place(titleCard, chips[i], 20 + i % 2 * 194, 170 + i / 2 * 30, 186f, 26f);
				chips[i].Click += delegate
				{
					ChipMenu(idx);
				};
			}

			// Recuadro de abajo: una fila por plataforma (activar + Conectar/Desconectar).
			Card platformsCard = new Card();
			Place(this, platformsCard, 20f, y0 + 248, 420f, 196f, PageTitle);
			string[] names = new string[4] { "Twitch", "YouTube", "Kick", "TikTok" };
			for (int i = 0; i < 4; i++)
			{
				int idx = i;
				tgPlat[i] = new Toggle
				{
					Text = names[i],
					Hint = ""
				};
				Place(platformsCard, tgPlat[i], 18f, 12 + i * 44, 270f, 42f);
				tgPlat[i].CheckedChanged += delegate
				{
					if (idx < 3)
					{
						plats[idx].Enabled = tgPlat[idx].Checked;
					}
					else
					{
						ttEnabled = tgPlat[idx].Checked;
					}
					RefreshTitleUi();
					if (!loading)
					{
						SaveConfig();
					}
				};
				btnConn[i] = new VButton
				{
					Kind = ButtonKind.Ghost,
					FontSize = 8.5f
				};
				Place(platformsCard, btnConn[i], 296f, 14 + i * 44, 104f, 32f);
				btnConn[i].Click += delegate
				{
					if (idx < 3)
					{
						ConnectClicked(plats[idx]);
					}
					else
					{
						// TikTok: no hay conexión, el botón copia el título.
						CopyText(fldTitle.Box.Text.Trim(), btnConn[3], "Copiar título");
					}
				};
			}
			btnUpdateTitle = new VButton
			{
				Kind = ButtonKind.Primary,
				FontSize = 10.5f
			};
			Place(this, btnUpdateTitle, 20f, y0 + 456, 420f, 44f, PageTitle);
			btnUpdateTitle.Click += delegate
			{
				UpdateTitles();
			};
			// La primera vez que se abre la pestaña, lee el título que hay ahora en la plataforma.
			tabs.SelectedChanged += delegate
			{
				if (PageOfTab(tabs.Selected) == PageTitle)
				{
					RefreshTitleUi();
					ReadCurrentOnce();
				}
			};
			RefreshTitleUi();
		}

		// Máximo de caracteres del título: el menor de las plataformas activas y conectadas.
		private int TitleMax()
		{
			int max = 140;
			foreach (Platform p in plats)
			{
				if (p.Enabled && p.Connected)
				{
					max = Math.Min(max, p.TitleMax);
				}
			}
			return max;
		}

		// Pone al día interruptores, botones y chips con el estado de cada plataforma.
		private void RefreshTitleUi()
		{
			if (plats == null || tgPlat[0] == null)
			{
				return;
			}
			for (int i = 0; i < 3; i++)
			{
				Platform p = plats[i];
				if (tgPlat[i].Checked != p.Enabled)
				{
					// Cambiarlo desde aquí no debe guardar la configuración.
					bool wasLoading = loading;
					loading = true;
					tgPlat[i].Checked = p.Enabled;
					loading = wasLoading;
				}
				tgPlat[i].Hint = (p.Connected ? ("Conectado" + ((p.Account.Length > 0) ? (" · " + p.Account) : "")) : (p.Configured ? "Sin conectar" : "Sin configurar"));
				tgPlat[i].Invalidate();
				btnConn[i].Text = (p.Connected ? "Desconectar" : "Conectar");
				btnConn[i].Kind = (p.Connected ? ButtonKind.Ghost : ButtonKind.Primary);
				btnConn[i].Invalidate();
				string category = ((p.Chosen != null) ? p.Chosen.Name : (p.Connected ? "elige categoría" : "sin conectar"));
				chips[i].Text = p.Name.ToUpperInvariant() + " · " + category;
				chips[i].Selected = p.Enabled && p.Connected && p.Chosen != null;
				chips[i].Enabled = p.Enabled;
				chips[i].Invalidate();
			}
			// TikTok (fila 3): solo copiar y pegar.
			if (tgPlat[3].Checked != ttEnabled)
			{
				bool wasLoading = loading;
				loading = true;
				tgPlat[3].Checked = ttEnabled;
				loading = wasLoading;
			}
			tgPlat[3].Hint = "Manual: copia y pega en LIVE Studio";
			tgPlat[3].Invalidate();
			if (!btnConn[3].Text.StartsWith("¡"))
			{
				btnConn[3].Text = "Copiar título";
			}
			btnConn[3].Kind = ButtonKind.Ghost;
			btnConn[3].Invalidate();
			chips[3].Text = "TIKTOK · COPIAR Y PEGAR";
			chips[3].Selected = false;
			chips[3].Enabled = ttEnabled;
			chips[3].Invalidate();
			int connected = 0;
			foreach (Platform p in plats)
			{
				if (p.Enabled && p.Connected)
				{
					connected++;
				}
			}
			btnUpdateTitle.Text = (busyTitle ? "Actualizando…" : ((connected != 0) ? ("Actualizar en " + connected + ((connected == 1) ? " plataforma" : " plataformas")) : (ttEnabled ? "Copiar título para TikTok" : "Conecta una plataforma")));
			btnUpdateTitle.Muted = busyTitle || (connected == 0 && !ttEnabled);
			btnUpdateTitle.Invalidate();
			titleCounter.Invalidate();
		}

		// ---------- Leer y buscar ----------

		// Lee una sola vez el título actual de la primera plataforma conectada (YouTube no sirve para esto).
		private void ReadCurrentOnce()
		{
			if (titleRead)
			{
				return;
			}
			Platform source = null;
			foreach (Platform p in plats)
			{
				if (p.Connected && p.Enabled && p != youtube)
				{
					source = p;
					break;
				}
			}
			if (source != null)
			{
				titleRead = true;
				ReadCurrent(source);
			}
		}

		// Rellena título y categoría con lo que hay ahora en la plataforma (si los campos están vacíos).
		private void ReadCurrent(Platform p)
		{
			string title = null;
			Category cat = null;
			Background(delegate
			{
				p.ReadCurrent(out title, out cat);
			}, delegate(Exception err)
			{
				if (err != null)
				{
					AppendLog(p.Name + ": no se pudo leer el título actual (" + Why(err) + ").");
					RefreshTitleUi();
				}
				else
				{
					if (fldTitle.Box.Text.Trim().Length == 0 && !string.IsNullOrEmpty(title))
					{
						fldTitle.Box.Text = title;
					}
					if (cat != null && p.Chosen == null)
					{
						p.Chosen = cat;
					}
					if (cat != null && fldCat.Box.Text.Trim().Length == 0 && p != youtube)
					{
						fldCat.Box.Text = cat.Name;
					}
					SaveConfig();
					RefreshTitleUi();
				}
			});
		}

		// Busca la categoría escrita en Twitch y Kick a la vez y elige la que mejor coincide.
		private void SearchCategory()
		{
			string q = fldCat.Box.Text.Trim();
			if (q.Length == 0)
			{
				fldCat.Box.Focus();
				return;
			}
			int pending = 0;
			foreach (Platform candidate in new Platform[2] { twitch, kick })
			{
				if (!candidate.Enabled || !candidate.Connected)
				{
					continue;
				}
				Platform plat = candidate;
				List<Category> res = null;
				pending++;
				btnSearch.Text = "…";
				Background(delegate
				{
					res = plat.Search(q);
				}, delegate(Exception err)
				{
					if (err != null)
					{
						AppendLog(plat.Name + ": no se pudo buscar la categoría (" + Why(err) + ").");
					}
					else
					{
						plat.Results = res;
						plat.Chosen = Platform.Best(res, q);
						if (plat.Chosen == null)
						{
							AppendLog(plat.Name + ": no hay ninguna categoría que coincida con \"" + q + "\".");
						}
					}
					// Cuando terminan todas las búsquedas:
					if (--pending == 0)
					{
						btnSearch.Text = "Buscar";
						SuggestYouTube(q);
						SaveConfig();
					}
					RefreshTitleUi();
				});
			}
			if (pending == 0)
			{
				SuggestYouTube(q);
				RefreshTitleUi();
				SaveConfig();
			}
		}

		// YouTube tiene una lista fija de categorías: propone la que corresponde al juego
		// (salvo que el usuario la haya elegido a mano).
		private void SuggestYouTube(string q)
		{
			if (!ytManual)
			{
				string gameName = ((twitch.Chosen != null) ? twitch.Chosen.Name : ((kick.Chosen != null) ? kick.Chosen.Name : q));
				Category category = YouTubePlatform.Suggest(gameName);
				if (category != null)
				{
					youtube.Chosen = category;
				}
			}
		}

		// Menú al pulsar un chip: elegir otra categoría de los resultados, o no cambiarla.
		private void ChipMenu(int i)
		{
			if (i == 3)
			{
				CopyText(fldTitle.Box.Text.Trim(), btnConn[3], "Copiar título");
				return;
			}
			Platform p = plats[i];
			if (!p.Connected)
			{
				ConnectClicked(p);
				return;
			}
			List<Category> list = ((p == youtube) ? new List<Category>(YouTubePlatform.All) : p.Results);
			ContextMenuStrip menu = DarkMenu();
			if (list.Count == 0)
			{
				ToolStripMenuItem hint = new ToolStripMenuItem(I18n.Tr("Escribe una categoría y pulsa Buscar"))
				{
					Enabled = false
				};
				menu.Items.Add(hint);
			}
			foreach (Category item in list)
			{
				Category cat = item;
				ToolStripMenuItem option = new ToolStripMenuItem(item.Name)
				{
					Checked = p.Chosen != null && p.Chosen.Id == item.Id
				};
				option.Click += delegate
				{
					p.Chosen = cat;
					if (p == youtube)
					{
						ytManual = true;
					}
					RefreshTitleUi();
					SaveConfig();
				};
				menu.Items.Add(option);
			}
			if (p.Chosen != null)
			{
				menu.Items.Add(new ToolStripSeparator());
				ToolStripMenuItem none = new ToolStripMenuItem(I18n.Tr("No cambiar la categoría en ") + p.Name);
				none.Click += delegate
				{
					p.Chosen = null;
					if (p == youtube)
					{
						ytManual = true;
					}
					RefreshTitleUi();
					SaveConfig();
				};
				menu.Items.Add(none);
			}
			menu.Show(chips[i], new Point(0, chips[i].Height + 2));
		}

		// Menú "Recientes": los últimos 10 títulos usados.
		private void ShowRecent()
		{
			ContextMenuStrip menu = DarkMenu();
			if (recentTitles.Count == 0)
			{
				menu.Items.Add(new ToolStripMenuItem(I18n.Tr("Aún no hay títulos usados"))
				{
					Enabled = false
				});
			}
			foreach (string recentTitle in recentTitles)
			{
				string title = recentTitle;
				ToolStripMenuItem option = new ToolStripMenuItem((recentTitle.Length > 60) ? (recentTitle.Substring(0, 57) + "…") : recentTitle);
				option.Click += delegate
				{
					fldTitle.Box.Text = title;
				};
				menu.Items.Add(option);
			}
			menu.Show(btnRecent, new Point(0, btnRecent.Height + 2));
		}

		// ---------- Actualizar ----------

		// Botón "Actualizar en N plataformas": cambia título y categoría en todas a la vez.
		private void UpdateTitles()
		{
			if (busyTitle)
			{
				return;
			}
			string title = fldTitle.Box.Text.Trim();
			List<Platform> targets = new List<Platform>();
			foreach (Platform p in plats)
			{
				if (p.Enabled)
				{
					if (!p.Connected)
					{
						AppendLog(p.Name + ": sin conectar (pulsa Conectar en Directo → Título y categoría).");
					}
					else
					{
						targets.Add(p);
					}
				}
			}
			if (title.Length == 0 && targets.Exists((Platform p) => p.Chosen == null))
			{
				Toast("Escribe un título (o elige una categoría) antes de actualizar.", "warn");
				fldTitle.Box.Focus();
				return;
			}
			if (ttEnabled && title.Length > 0)
			{
				try
				{
					Clipboard.SetText(title);
				}
				catch
				{
				}
				AppendLog("TikTok: título copiado. Pégalo en TikTok LIVE Studio (TikTok no permite cambiarlo desde otros programas).");
			}
			if (title.Length > 0)
			{
				recentTitles.Remove(title);
				recentTitles.Insert(0, title);
				while (recentTitles.Count > 10)
				{
					recentTitles.RemoveAt(recentTitles.Count - 1);
				}
				SaveConfig();
			}
			if (targets.Count == 0)
			{
				RefreshTitleUi();
				return;
			}
			busyTitle = true;
			RefreshTitleUi();
			int pending = targets.Count;
			int ok = 0;
			foreach (Platform item in targets)
			{
				Platform plat = item;
				// Cada plataforma tiene su máximo de caracteres: se recorta si hace falta.
				string t = ((title.Length == 0) ? null : ((title.Length > plat.TitleMax) ? title.Substring(0, plat.TitleMax) : title));
				if (t != null && t.Length < title.Length)
				{
					AppendLog(plat.Name + ": el título se recorta a " + plat.TitleMax + " caracteres.");
				}
				Background(delegate
				{
					plat.Update(t, plat.Chosen);
				}, delegate(Exception err)
				{
					if (err == null)
					{
						ok++;
						AppendLog(plat.Name + ": título" + ((plat.Chosen != null) ? (" y categoría (" + plat.Chosen.Name + ")") : "") + " actualizados.");
					}
					else
					{
						AppendLog(plat.Name + ": no se pudo actualizar (" + Why(err) + ").");
					}
					if (--pending <= 0)
					{
						// Todas han respondido: el botón muestra el resultado 2,5 s.
						busyTitle = false;
						RefreshTitleUi();
						btnUpdateTitle.Text = ((ok == targets.Count) ? "¡Actualizado!" : "Con errores: mira el Registro");
						btnUpdateTitle.Invalidate();
						System.Windows.Forms.Timer t2 = new System.Windows.Forms.Timer
						{
							Interval = 2500
						};
						t2.Tick += delegate
						{
							t2.Dispose();
							RefreshTitleUi();
						};
						t2.Start();
					}
				});
			}
		}

		// ---------- Conectar plataformas ----------

		// Botón Conectar/Desconectar. Si aún no hay app creada, primero pide sus datos.
		private void ConnectClicked(Platform p)
		{
			if (p.Connected)
			{
				if (I18n.Show(this, "¿Desconectar " + p.Name + ((p.Account.Length > 0) ? (" (" + p.Account + ")") : "") + "?\n\nTendrás que volver a autorizarlo para cambiar el título.", "Desconectar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					p.Logout();
					SaveConfig();
					RefreshTitleUi();
					AppendLog(p.Name + ": desconectado.");
				}
			}
			else if (p.Configured || SetupDialog(p))
			{
				SaveConfig();
				LoginDialog(p);
			}
		}

		// Pasos para crear la app de cada plataforma (se enseñan en la ventana "Configurar").
		private static string[] SetupSteps(Platform p)
		{
			if (p is TwitchPlatform)
			{
				return new string[3] { "1. Abre el panel de desarrolladores de Twitch (botón de abajo) e inicia sesión. Twitch exige tener activada la verificación en dos pasos.", "2. Pulsa \"Registrar tu aplicación\" y rellena: Nombre: Vortexs Live-tucanal · URL de redirección OAuth: http://localhost · Categoría: Application Integration · Tipo de cliente: Público.", "3. Pulsa Crear, después Administrar, y copia aquí el \"ID de cliente\"." };
			}
			if (p is KickPlatform)
			{
				return new string[3]
				{
					"1. Abre Kick → Ajustes → Developer (botón de abajo) y pulsa Create App. Kick exige la verificación en dos pasos.",
					"2. Redirect URL: http://localhost:" + KickPlatform.CallbackPort + "/callback (cópiala con el botón). Marca los permisos: Read user information, Read channel information, Write to channel information y el de escribir en el chat (chat:write, para el chat conjunto).",
					"3. Guarda la app y copia aquí el Client ID y el Client Secret."
				};
			}
			return new string[3] { "1. Abre Google Cloud (botón de abajo), crea un proyecto \"Vortexs Live\" y en Biblioteca habilita \"YouTube Data API v3\".", "2. Pantalla de consentimiento / Google Auth Platform: tipo Externo, pon un nombre y tu correo. En Público, pulsa \"Publicar aplicación\" (si no, Google te desconecta cada 7 días).", "3. Credenciales → Crear credenciales → ID de cliente OAuth → tipo \"TVs y dispositivos de entrada limitada\". Copia aquí el ID de cliente y el secreto." };
		}

		// Ventana "Configurar": explica cómo crear la app y pide el ID (y el secreto) de cliente.
		// Devuelve true si se guardaron los datos.
		private bool SetupDialog(Platform p)
		{
			Form f = DarkForm("Configurar " + p.Name, 460, p.NeedsSecret ? 548 : 470);
			string[] steps = SetupSteps(p);
			Card card = new Card
			{
				Title = "Crea tu app de " + p.Name,
				Subtitle = "Solo una vez. Es gratis y tarda unos minutos."
			};
			Place(f, card, 20f, 18f, 420f, 268f);
			PaintBox stepsBox = new PaintBox
			{
				Painter = delegate(Graphics g, Rectangle r)
				{
					float y = 0f;
					foreach (string step in steps)
					{
						SizeF size = g.MeasureString(step, Theme.Body(8.5f), r.Width);
						Theme.DrawWrapped(g, step, Theme.Body(8.5f), Theme.Text, new RectangleF(0f, y, r.Width, size.Height + 2f));
						y += size.Height + (float)S(8f);
					}
				}
			};
			Place(card, stepsBox, 20f, 62f, 380f, 160f);
			VButton openPanel = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Abrir panel de " + p.Name,
				FontSize = 9f
			};
			Place(card, openPanel, 20f, 220f, 200f, 34f);
			openPanel.Click += delegate
			{
				Http.OpenBrowser(p.ConsoleUrl);
			};
			string redirect = ((p is KickPlatform) ? KickPlatform.RedirectUri : ((p is TwitchPlatform) ? "http://localhost" : null));
			if (redirect != null)
			{
				VButton cr = new VButton
				{
					Kind = ButtonKind.Ghost,
					Text = "Copiar URL de redirección",
					FontSize = 8.5f
				};
				Place(card, cr, 228f, 220f, 172f, 34f);
				cr.Click += delegate
				{
					CopyText(redirect, cr, "Copiar URL de redirección");
				};
			}
			int top = 300;
			Label(f, (p is TwitchPlatform) ? "ID DE CLIENTE" : "CLIENT ID", 22f, top, 300f);
			Field id = new Field();
			Place(f, id, 20f, top + 18, 420f, 36f);
			id.Box.Text = p.ClientId;
			Field sec = null;
			top += 66;
			if (p.NeedsSecret)
			{
				Label(f, "CLIENT SECRET", 22f, top, 300f);
				sec = new Field();
				Place(f, sec, 20f, top + 18, 420f, 36f);
				sec.Box.UseSystemPasswordChar = true;
				sec.Box.Text = p.ClientSecret;
				top += 66;
			}
			Box(f, 22f, top, 416f, 34f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Se guardan cifrados en VortexsLive.ini (solo tu usuario de Windows puede leerlos). No compartas ese archivo.", Theme.Body(7.75f), Theme.Muted, r);
			});
			VButton save = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Guardar y conectar",
				FontSize = 10f
			};
			VButton cancel = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Cancelar",
				FontSize = 10f
			};
			Place(f, cancel, 20f, top + 44, 130f, 42f);
			Place(f, save, 160f, top + 44, 280f, 42f);
			cancel.Click += delegate
			{
				f.DialogResult = DialogResult.Cancel;
				f.Close();
			};
			save.Click += delegate
			{
				string newId = id.Box.Text.Trim();
				string newSecret = ((sec != null) ? sec.Box.Text.Trim() : "");
				if (newId.Length < 8 || (p.NeedsSecret && newSecret.Length < 8))
				{
					I18n.Show(f, p.NeedsSecret ? "Pega el Client ID y el Client Secret." : "Pega el ID de cliente.", "Faltan datos");
				}
				else
				{
					// Con otra app, la sesión anterior ya no vale.
					if (newId != p.ClientId)
					{
						p.Logout();
					}
					p.ClientId = newId;
					p.ClientSecret = newSecret;
					f.DialogResult = DialogResult.OK;
					f.Close();
				}
			};
			return f.ShowDialog(this) == DialogResult.OK;
		}

		// Ventana "Conectar": inicia sesión en la plataforma. Según la plataforma, enseña un código
		// para escribir en su web o abre el navegador y espera a que el usuario pulse "Permitir".
		private void LoginDialog(Platform p)
		{
			Form f = DarkForm("Conectar " + p.Name, 420, 330);
			string code = "";
			string url = "";
			string status = "Preparando…";
			bool showCode = false;
			Card card = new Card
			{
				Title = "Conectar " + p.Name
			};
			Place(f, card, 20f, 18f, 380f, 226f);
			PaintBox body = new PaintBox();
			Place(card, body, 20f, 50f, 340f, 166f);
			body.Painter = delegate(Graphics g, Rectangle r)
			{
				if (showCode)
				{
					Theme.DrawWrapped(g, "Se ha abierto la página de " + p.Name + " (y el código está copiado). Inicia sesión y escribe este código:", Theme.Body(8.75f), Theme.Muted, new RectangleF(0f, 0f, r.Width, S(40f)));
					Font font = Theme.Display(26f);
					SizeF size = Theme.Measure(g, code, font);
					RectangleF codeRect = new RectangleF(((float)r.Width - size.Width) / 2f, S(54f), size.Width, size.Height);
					using (LinearGradientBrush brush = Theme.Brand(codeRect, true))
					{
						g.DrawString(code, font, brush, codeRect.X, codeRect.Y, StringFormat.GenericTypographic);
					}
					Theme.DrawText(g, status, Theme.Mono(7.5f), Theme.Dim, new RectangleF(0f, S(118f), r.Width, S(20f)), StringAlignment.Center, StringAlignment.Center);
				}
				else
				{
					Theme.DrawWrapped(g, status, Theme.Body(9f), Theme.Text, new RectangleF(0f, 0f, r.Width, r.Height));
				}
			};
			VButton openB = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Abrir página",
				FontSize = 9.5f
			};
			VButton copyB = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar código",
				FontSize = 9.5f
			};
			VButton cancelB = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Cancelar",
				FontSize = 9.5f
			};
			Place(f, cancelB, 20f, 262f, 110f, 44f);
			Place(f, copyB, 138f, 262f, 126f, 44f);
			Place(f, openB, 272f, 262f, 128f, 44f);
			copyB.Enabled = false;
			openB.Enabled = false;
			openB.Click += delegate
			{
				if (url.Length > 0)
				{
					Http.OpenBrowser(url);
				}
			};
			copyB.Click += delegate
			{
				CopyText(code, copyB, "Copiar código");
			};
			// La plataforma inicia sesión en segundo plano y avisa por aquí de lo que hay que enseñar.
			LoginUi ui = new LoginUi();
			ui.OnCode = delegate(string c, string u)
			{
				f.BeginInvoke((Action)delegate
				{
					code = c;
					url = u;
					showCode = true;
					status = "ESPERANDO A QUE LO APRUEBES…";
					copyB.Enabled = true;
					openB.Enabled = true;
					try
					{
						Clipboard.SetText(c);
					}
					catch
					{
					}
					Http.OpenBrowser(u);
					body.Invalidate();
				});
			};
			ui.OnBrowser = delegate(string u)
			{
				f.BeginInvoke((Action)delegate
				{
					url = u;
					status = "Se ha abierto el navegador. Inicia sesión en " + p.Name + " y pulsa \"Allow\" / \"Permitir\".\n\nCuando termines, esta ventana se cerrará sola.";
					openB.Text = "Abrir de nuevo";
					openB.Enabled = true;
					copyB.Visible = false;
					body.Invalidate();
				});
			};
			cancelB.Click += delegate
			{
				ui.Cancel = true;
				f.Close();
			};
			f.FormClosing += delegate
			{
				ui.Cancel = true;
			};
			bool done = false;
			f.Shown += delegate
			{
				Background(delegate
				{
					p.Login(ui);
				}, delegate(Exception err)
				{
					done = true;
					if (err == null)
					{
						AppendLog(p.Name + ": conectado" + ((p.Account.Length > 0) ? (" como " + p.Account) : "") + ".");
						SaveConfig();
						RefreshTitleUi();
						if (!f.IsDisposed)
						{
							f.Close();
						}
						ReadCurrent(p);
					}
					else if (!(err is OperationCanceledException))
					{
						AppendLog(p.Name + ": no se pudo conectar (" + Why(err) + ").");
						if (!f.IsDisposed)
						{
							showCode = false;
							// Un error 4xx casi siempre es un ID o secreto de cliente mal copiado.
							status = "No se pudo conectar: " + Why(err) + ((err is ApiException && ((ApiException)err).Status >= 400 && ((ApiException)err).Status < 500) ? ("\n\nRevisa el ID de cliente" + (p.NeedsSecret ? " y el secreto" : "") + " (Configurar de nuevo: desconecta y vuelve a conectar).") : "");
							copyB.Enabled = false;
							openB.Enabled = false;
							cancelB.Text = "Cerrar";
							body.Invalidate();
							if (Why(err).ToLowerInvariant().Contains("client"))
							{
								// La plataforma no reconoce la app: la próxima vez se vuelve a pedir.
								p.ClientId = "";
								SaveConfig();
							}
						}
					}
					RefreshTitleUi();
				});
			};
			f.ShowDialog(this);
			if (!done)
			{
				ui.Cancel = true;
			}
		}

		// ---------- Configuración (VortexsLive.ini) ----------

		private void LoadTitleConfig(Dictionary<string, string> raw)
		{
			string value;
			Func<string, string> get = (string k) => (!raw.TryGetValue(k, out value)) ? "" : value;
			foreach (Platform p in plats)
			{
				p.Load(get);
			}
			fldTitle.Box.Text = get("title");
			fldCat.Box.Text = get("cat_query");
			ttEnabled = get("tt_on") != "0";
			ytManual = get("yt_manual") == "1";
			recentTitles.Clear();
			// Los títulos recientes van juntos en Base64 (pueden llevar cualquier carácter).
			string recent = get("title_recent");
			if (recent.Length > 0)
			{
				try
				{
					foreach (string line in Encoding.UTF8.GetString(Convert.FromBase64String(recent)).Split('\n'))
					{
						if (line.Trim().Length > 0)
						{
							recentTitles.Add(line);
						}
					}
				}
				catch
				{
				}
			}
			RefreshTitleUi();
		}

		private void SaveTitleConfig(List<string> lines)
		{
			if (plats != null && fldTitle != null)
			{
				foreach (Platform p in plats)
				{
					p.Save(lines);
				}
				lines.Add("title=" + fldTitle.Box.Text.Replace("\r", " ").Replace("\n", " "));
				lines.Add("cat_query=" + fldCat.Box.Text.Replace("\n", " "));
				lines.Add("tt_on=" + (ttEnabled ? "1" : "0"));
				lines.Add("yt_manual=" + (ytManual ? "1" : "0"));
				lines.Add("title_recent=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", recentTitles.ToArray()))));
			}
		}
	}
}
