// Licencia en la ventana: el botón del plan (arriba), la ventana "Licencia", el aviso de las
// pestañas que son del plan Completo y lo que se guarda. Las reglas están en License.cs.
//
// Qué se guarda:
//   VortexsLive.ini   lic= (la clave, cifrada), lic_act= (activación de Polar en este PC),
//                     lic_ok= (última comprobación de Polar, cifrada), t0= (inicio de la prueba)
//   Registro de Windows (HKCU\Software\Vortexs\Vortexs Live): inicio de la prueba y la última vez
//                     que se abrió, para que reinstalar o borrar el .ini no dé otra prueba.
//   vortexs.es/api/prueba: la prueba también se apunta en la web, por PC (la huella de MachineId).
//                     Así ni borrar el registro ni usar otro usuario de Windows dan otra prueba.
//                     La primera vez hace falta internet; luego se guarda (t0s= y "s") y ya no.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Net;
using Microsoft.Win32;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private const int PageLocked = 7;

		// Se mantiene el nombre antiguo (StreamDelay) para que actualizar no dé otra prueba.
		private const string RegPath = "Software\\Vortexs\\StreamDelay";

		// Plan con el que funciona ahora el programa.
		private Plan plan = Plan.Full;

		// De dónde sale ese plan (prueba o clave), para enseñarlo.
		private LicenseInfo planInfo = new LicenseInfo();

		// Por qué la clave guardada no vale (si no vale).
		private string licProblem = "";

		private string licKey = "";

		private string licActivation = "";

		// Polar: "plan|ticks UTC de la última comprobación buena|correo".
		private string licCache = "";

		private DateTime trialStart = DateTime.UtcNow;

		// Inicio de la prueba confirmado por vortexs.es (MinValue = aún no). Sin él, la prueba solo
		// vale mientras se pregunta; si no hay internet, no hay prueba (sí planes de pago).
		private const string TrialUrl = "https://www.vortexs.es/api/prueba";

		private DateTime trialOk = DateTime.MinValue;

		private bool trialAsking;

		private bool trialFailed;

		private bool trialFailLogged;

		private DateTime trialNextAsk = DateTime.MinValue;

		private bool clockBack;

		private bool licChecking;

		private DateTime licNextCheck = DateTime.MinValue;

		private DateTime licNextTick = DateTime.MinValue;

		// Avisos ya enseñados: "h" bienvenida, "a" queda menos de 1 día, "f" la prueba ha terminado.
		private string licSeen = "";

		private bool licNoticeOpen;

		// Ya se ha leído la licencia (antes no se escribe en el registro, para no borrarla).
		private bool licLoaded;

		// La clave de Polar que valía ha dejado de valer: falta avisar.
		private bool licLost;

		private VButton btnPlan;

		private PaintBox lockBox;

		// Pestaña que se quería abrir cuando salió el aviso del plan Completo.
		private int lockedPage = PageChat;

		// ---------- Guardar y cargar ----------

		private void LoadLicenseConfig(Dictionary<string, string> raw)
		{
			string v;
			licKey = raw.TryGetValue("lic", out v) ? Secret.Unprotect(v) : "";
			licActivation = raw.TryGetValue("lic_act", out v) ? v.Trim() : "";
			licCache = raw.TryGetValue("lic_ok", out v) ? Secret.Unprotect(v) : "";
			licSeen = raw.TryGetValue("lic_avisos", out v) ? v.Trim() : "";
			// La licencia también se guarda en el registro de Windows de este PC: así sigue activada
			// aunque se borre el .ini o se reinstale el programa en otra carpeta.
			if (licKey.Length == 0)
			{
				try
				{
					using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath))
					{
						if (k != null)
						{
							licKey = Secret.Unprotect(k.GetValue("k") as string);
							licActivation = (k.GetValue("a") as string) ?? "";
							licCache = Secret.Unprotect(k.GetValue("c") as string);
						}
					}
				}
				catch
				{
				}
			}
			licLoaded = true;
			// Inicio de la prueba: el más antiguo entre el .ini y el registro de Windows.
			DateTime now = DateTime.UtcNow;
			DateTime start = now;
			DateTime seen = DateTime.MinValue;
			DateTime t;
			if (raw.TryGetValue("t0", out v) && TryTicks(Secret.Unprotect(v), out t) && t < start)
			{
				start = t;
			}
			if (raw.TryGetValue("t0s", out v) && TryTicks(Secret.Unprotect(v), out t))
			{
				trialOk = t;
			}
			try
			{
				using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
				{
					if (TryTicks(Secret.Unprotect(k.GetValue("p") as string), out t) && t < start)
					{
						start = t;
					}
					if (TryTicks(Secret.Unprotect(k.GetValue("v") as string), out t))
					{
						seen = t;
					}
					if (TryTicks(Secret.Unprotect(k.GetValue("s") as string), out t) && (trialOk == DateTime.MinValue || t < trialOk))
					{
						trialOk = t;
					}
					if (trialOk != DateTime.MinValue && trialOk < start)
					{
						start = trialOk;
					}
					k.SetValue("p", Secret.Protect(start.Ticks.ToString(CultureInfo.InvariantCulture)));
				}
			}
			catch
			{
			}
			trialStart = start;
			// Si el reloj del PC va más de un día por detrás de la última vez, la prueba no vale.
			clockBack = seen > now.AddDays(1.0);
			ComputePlan(false);
		}

		private void SaveLicenseConfig(List<string> lines)
		{
			lines.Add("lic=" + Secret.Protect(licKey));
			lines.Add("lic_act=" + licActivation);
			lines.Add("lic_ok=" + Secret.Protect(licCache));
			lines.Add("t0=" + Secret.Protect(trialStart.Ticks.ToString(CultureInfo.InvariantCulture)));
			lines.Add("t0s=" + ((trialOk == DateTime.MinValue) ? "" : Secret.Protect(trialOk.Ticks.ToString(CultureInfo.InvariantCulture))));
			lines.Add("lic_avisos=" + licSeen);
			if (!licLoaded)
			{
				return;
			}
			try
			{
				using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
				{
					k.SetValue("k", Secret.Protect(licKey));
					k.SetValue("a", licActivation);
					k.SetValue("c", Secret.Protect(licCache));
				}
			}
			catch
			{
			}
		}

		// Huella de este PC para atar la activación de Polar: no se puede copiar a otro equipo.
		// Es el identificador de la instalación de Windows (MachineGuid), resumido; si no se puede leer,
		// el nombre del equipo.
		private static string machineId;

		private static string MachineId()
		{
			if (machineId != null)
			{
				return machineId;
			}
			string raw = null;
			try
			{
				using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
				{
					using (RegistryKey k = hklm.OpenSubKey("SOFTWARE\\Microsoft\\Cryptography"))
					{
						if (k != null)
						{
							raw = k.GetValue("MachineGuid") as string;
						}
					}
				}
			}
			catch
			{
			}
			if (string.IsNullOrEmpty(raw))
			{
				raw = Environment.MachineName;
			}
			using (System.Security.Cryptography.SHA256 sha = new System.Security.Cryptography.SHA256Managed())
			{
				byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("VortexsLive|" + raw.ToLowerInvariant()));
				machineId = BitConverter.ToString(h, 0, 12).Replace("-", "").ToLowerInvariant();
			}
			return machineId;
		}

		private static Dictionary<string, object> MachineConditions()
		{
			return new Dictionary<string, object> { { "equipo", MachineId() } };
		}

		private static bool TryTicks(string s, out DateTime t)
		{
			long ticks;
			t = DateTime.MinValue;
			if (string.IsNullOrEmpty(s) || !long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) || ticks <= 0 || ticks > DateTime.MaxValue.Ticks)
			{
				return false;
			}
			t = new DateTime(ticks, DateTimeKind.Utc);
			return true;
		}

		// ---------- Calcular el plan ----------

		// Lo que da la clave guardada (sin internet: las de Polar, según la última comprobación).
		private LicenseInfo KeyInfo(DateTime now)
		{
			if (licKey.Length == 0)
			{
				return null;
			}
			if (License.IsSigned(licKey))
			{
				return License.CheckSigned(licKey, now);
			}
			string[] c = licCache.Split('|');
			DateTime when;
			if (c.Length < 3 || !TryTicks(c[1], out when))
			{
				return LicenseInfo.Bad("falta comprobar la clave por internet");
			}
			LicenseInfo info = new LicenseInfo
			{
				Source = "polar",
				Name = c[2],
				Gift = c.Length > 3 && c[3] == "r",
				Plan = (Plan)Math.Max(0, Math.Min(2, (int)(c[0][0] - '0')))
			};
			if (now - when > TimeSpan.FromDays(7.0))
			{
				info.Plan = Plan.None;
				info.Error = "hace más de 7 días que no se puede comprobar la suscripción (¿hay internet?)";
			}
			return info;
		}

		// Elige el mejor entre la prueba y la clave. Con apply, quita lo del plan Completo si ya no lo hay.
		private void ComputePlan(bool apply)
		{
			DateTime now = DateTime.UtcNow;
			LicenseInfo trial = License.Trial(trialStart, now);
			if (clockBack)
			{
				trial = LicenseInfo.Bad("la fecha del PC está atrasada");
				trial.Source = "prueba";
			}
			else if (trialOk == DateTime.MinValue && trialFailed)
			{
				trial = LicenseInfo.Bad("para empezar la prueba hace falta internet (solo la primera vez)");
				trial.Source = "prueba";
			}
			LicenseInfo key = KeyInfo(now);
			licProblem = (key != null) ? key.Error : "";
			LicenseInfo best = (key != null && key.Plan != Plan.None && key.Plan >= trial.Plan) ? key : trial;
			if (key != null && key.Plan == Plan.None && trial.Plan == Plan.None)
			{
				best = key;
			}
			Plan before = plan;
			plan = best.Plan;
			planInfo = best;
			try
			{
				using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
				{
					k.SetValue("v", Secret.Protect(now.Ticks.ToString(CultureInfo.InvariantCulture)));
				}
			}
			catch
			{
			}
			if (btnPlan != null)
			{
				string text = PlanButtonText();
				if (btnPlan.Text != text)
				{
					btnPlan.Text = text;
					btnPlan.Invalidate();
				}
			}
			if (!apply || before == plan)
			{
				return;
			}
			if (before == Plan.Full && plan != Plan.Full)
			{
				AppendLog(plan == Plan.Basic ? "Licencia: ahora tienes el plan Básico. Clips, Chat, Overlays y Mando son del plan Completo." : "Licencia: " + (planInfo.Error.Length > 0 ? planInfo.Error : "no hay ningún plan activo") + ". Elige un plan con el botón de arriba.");
				if (chatHub != null)
				{
					StopChat();
					loading = true;
					tgChat.Checked = false;
					loading = false;
				}
			}
			else if (plan > before)
			{
				AppendLog("Licencia: plan " + License.PlanName(plan) + " activado.");
			}
			if (tabs != null)
			{
				ShowPage(PageOfTab(tabs.Selected));
			}
		}

		private string PlanButtonText()
		{
			if (plan == Plan.None)
			{
				return "Activar licencia";
			}
			if (planInfo.Source == "prueba")
			{
				return "Prueba · " + License.Left(planInfo.Expires, DateTime.UtcNow);
			}
			return "Plan " + License.PlanName(plan);
		}

		// Cada medio segundo desde el reloj de la ventana: cada minuto se recalcula el plan (la prueba
		// se acaba sola) y cada 6 horas se vuelve a comprobar la suscripción de Polar.
		private void LicenseTick()
		{
			if (Program.CaptureDir != null)
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			if (trialOk == DateTime.MinValue && !trialAsking && now >= trialNextAsk)
			{
				AskTrial();
			}
			if (now < licNextTick)
			{
				return;
			}
			licNextTick = now.AddMinutes(1.0);
			// Mientras se emite no se quita nada: el cambio espera a que se acabe el directo.
			if (!engine.OutputActive)
			{
				ComputePlan(true);
			}
			if (licKey.Length > 0 && !License.IsSigned(licKey) && License.PolarReady && now >= licNextCheck && !licChecking)
			{
				CheckPolar(null);
			}
			LicenseNotices();
			LicenseLostNotice();
		}

		// ---------- Avisos de la prueba ----------
		// Cada uno sale una sola vez, nunca en pleno directo, ni encima de otra ventana o aviso, ni si
		// Vortexs Live no es la ventana activa: si no se puede, espera al minuto siguiente.

		private void LicenseNotices()
		{
			// Hasta que la web confirme la prueba no se enseña ningún aviso de prueba.
			if (licNoticeOpen || !base.Visible || Form.ActiveForm != this || engine.OutputActive || base.OwnedForms.Length > 0 || planInfo.Source != "prueba" || trialOk == DateTime.MinValue)
			{
				return;
			}
			DateTime now = DateTime.UtcNow;
			if (plan == Plan.Full && licSeen.IndexOf('h') < 0)
			{
				MarkSeen("h");
				if (Notice("Bienvenido a Vortexs Live", "3 días gratis con todo",
					"Durante 3 días puedes usar Vortexs Live completo: delay dinámico, multistream, título, clips, chat conjunto, overlays y Mando.\n\nDespués elige un plan, mes a mes y sin permanencia:\n•  Básico, " + License.PriceBasic + ": delay dinámico, multistream y título.\n•  Completo, " + License.PriceFull + ": todo, también clips, chat, overlays y Mando.\n\nLos días que te quedan se ven arriba, debajo de «English». Si ya tienes una clave, pégala en «Ver planes o activar una clave».",
					"Empezar la prueba", "Ver planes o activar una clave"))
				{
					return;
				}
				LicenseDialog();
				return;
			}
			if (plan == Plan.Full && planInfo.Expires - now < TimeSpan.FromDays(1.0) && licSeen.IndexOf('a') < 0)
			{
				MarkSeen("a");
				if (!Notice("Prueba", "Te queda " + License.Left(planInfo.Expires, now) + " de prueba",
					"Cuando termine, para seguir emitiendo con delay necesitarás un plan:\n•  Básico, " + License.PriceBasic + ": delay dinámico, multistream y título.\n•  Completo, " + License.PriceFull + ": todo, también clips, chat, overlays y Mando.\n\nSi te suscribes ahora no pierdes nada: el plan empieza a contar desde que pagas.",
					"Ver planes", "Ahora no"))
				{
					return;
				}
				LicenseDialog();
				return;
			}
			if (plan == Plan.None && licSeen.IndexOf('f') < 0)
			{
				MarkSeen("f");
				if (!Notice("Prueba", "La prueba de 3 días ha terminado",
					"Gracias por probar Vortexs Live. Para seguir usándolo elige un plan, mes a mes y sin permanencia:\n•  Básico, " + License.PriceBasic + ": delay dinámico, multistream y título.\n•  Completo, " + License.PriceFull + ": todo, también clips, chat, overlays y Mando.\n\nTus ajustes se quedan guardados.",
					"Ver planes", "Ahora no"))
				{
					return;
				}
				LicenseDialog();
			}
		}

		// Aviso de que la licencia de Polar ha dejado de valer (cambio de plan, cancelación o cobro
		// fallido). Al cambiar de plan Polar da una clave NUEVA (la del otro plan) y anula la vieja.
		private void LicenseLostNotice()
		{
			if (!licLost || licNoticeOpen || !base.Visible || Form.ActiveForm != this || engine.OutputActive || base.OwnedForms.Length > 0)
			{
				return;
			}
			licLost = false;
			if (Notice("Licencia", "Tu licencia ya no está activa",
				"Puede ser por una de estas razones:\n•  Has cambiado de plan (por ejemplo, de Completo a Básico): Polar te ha dado una clave nueva. Está en el correo de Polar y en tu cuenta de cliente. Pégala en Licencia.\n•  Se ha cancelado la suscripción.\n•  No se ha podido cobrar el mes: en cuanto se cobre, tu misma clave vuelve a funcionar sola.",
				"Pegar clave nueva", "Ahora no"))
			{
				LicenseDialog();
			}
		}

		private void MarkSeen(string flag)
		{
			if (licSeen.IndexOf(flag, StringComparison.Ordinal) < 0)
			{
				licSeen += flag;
				SaveConfig();
			}
		}

		// Ventana de aviso con dos botones. Devuelve true si se pulsa el primero (el violeta).
		private bool Notice(string title, string heading, string text, string primary, string secondary)
		{
			bool first = false;
			licNoticeOpen = true;
			try
			{
				Form f = DarkForm(title, 440, 380);
				Box(f, 24f, 20f, 392f, 280f, delegate(Graphics g, Rectangle r)
				{
					Card.Glow(g, new RectangleF(-r.Width * 0.2f, -r.Height * 0.4f, r.Width * 0.9f, r.Height * 0.8f), Color.FromArgb(40, Theme.Violet));
					Theme.DrawText(g, "VORTEXS LIVE " + Version, Theme.Mono(7.5f), Theme.Violet, new RectangleF(0f, 0f, r.Width, S(18f)), StringAlignment.Near, StringAlignment.Center);
					Theme.DrawText(g, heading, Theme.Display(16f), Theme.Text, new RectangleF(0f, S(20f), r.Width, S(34f)), StringAlignment.Near, StringAlignment.Center);
					Theme.DrawWrapped(g, text, Theme.Body(9f), Theme.Muted, new RectangleF(0f, S(64f), r.Width, r.Height - S(64f)));
				});
				VButton b2 = new VButton { Kind = ButtonKind.Ghost, Text = secondary, FontSize = 9f };
				VButton b1 = new VButton { Kind = ButtonKind.Primary, Text = primary, FontSize = 10f };
				Place(f, b2, 24f, 312f, 210f, 44f);
				Place(f, b1, 244f, 312f, 172f, 44f);
				b1.Click += delegate
				{
					first = true;
					f.Close();
				};
				b2.Click += delegate
				{
					f.Close();
				};
				f.ShowDialog(this);
				f.Dispose();
			}
			finally
			{
				licNoticeOpen = false;
			}
			return first;
		}

		// ---------- Prueba apuntada en vortexs.es ----------

		// Pregunta a la web cuándo empezó la prueba este PC (en otro hilo). La web guarda la fecha
		// más antigua que conoce; la app se queda con la más antigua entre la suya y la de la web.
		private void AskTrial()
		{
			trialAsking = true;
			trialNextAsk = DateTime.UtcNow.AddMinutes(1.0);
			DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			long localMs = (long)(trialStart - epoch).TotalMilliseconds;
			string url = TrialUrl + "?equipo=" + MachineId() + "&inicio=" + localMs.ToString(CultureInfo.InvariantCulture);
			System.Threading.ThreadPool.QueueUserWorkItem(delegate
			{
				DateTime got = DateTime.MinValue;
				try
				{
					Dictionary<string, object> r = Http.Send("GET", url, null, null, null, null);
					object v;
					if (r != null && r.TryGetValue("inicio", out v) && v != null)
					{
						got = epoch.AddMilliseconds(Convert.ToDouble(v, CultureInfo.InvariantCulture));
					}
				}
				catch
				{
				}
				try
				{
					BeginInvoke((Action)delegate
					{
						TrialAnswer(got);
					});
				}
				catch
				{
					trialAsking = false;
				}
			});
		}

		private void TrialAnswer(DateTime got)
		{
			trialAsking = false;
			if (got == DateTime.MinValue || got > DateTime.UtcNow.AddDays(1.0))
			{
				trialFailed = true;
				if (!trialFailLogged)
				{
					trialFailLogged = true;
					AppendLog("Prueba gratis: no se ha podido conectar con vortexs.es. La primera vez hace falta internet para empezarla; se vuelve a intentar cada minuto.");
				}
			}
			else
			{
				trialFailed = false;
				trialOk = got;
				if (got < trialStart)
				{
					trialStart = got;
				}
				try
				{
					using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
					{
						k.SetValue("s", Secret.Protect(trialOk.Ticks.ToString(CultureInfo.InvariantCulture)));
						k.SetValue("p", Secret.Protect(trialStart.Ticks.ToString(CultureInfo.InvariantCulture)));
					}
				}
				catch
				{
				}
				SaveConfig();
				if (trialFailLogged)
				{
					AppendLog("Prueba gratis: conexión con vortexs.es recuperada.");
				}
			}
			// En pleno directo no se quita nada: el cambio espera (LicenseTick lo aplica después).
			if (!engine.OutputActive)
			{
				ComputePlan(true);
			}
		}

		// ---------- Polar ----------

		// Comprueba la clave en Polar (en otro hilo). done(null) si vale, o el motivo si no.
		private void CheckPolar(Action<string> done)
		{
			string key = licKey;
			string act = licActivation;
			string newAct = act;
			string newCache = null;
			string problem = null;
			bool offline = false;
			licChecking = true;
			licNextCheck = DateTime.UtcNow.AddHours(6.0);
			Background(delegate
			{
				LicenseInfo info;
				try
				{
					info = PolarValidate(key, act);
				}
				catch (ApiException ex)
				{
					if (ex.Status == 404 && act.Length > 0)
					{
						// La activación de este PC se borró (por ejemplo desde el portal): se vuelve a activar.
						act = "";
						newAct = "";
						info = PolarValidate(key, "");
					}
					else
					{
						throw;
					}
				}
				if (info.NeedsActivation)
				{
					Dictionary<string, object> body = new Dictionary<string, object>
					{
						{ "key", key },
						{ "organization_id", License.PolarOrg },
						{ "label", Environment.MachineName },
						{ "conditions", MachineConditions() },
						{ "meta", new Dictionary<string, object> { { "programa", "Vortexs Live " + Version } } }
					};
					Dictionary<string, object> a;
					try
					{
						a = Http.Send("POST", License.PolarApi + "/v1/customer-portal/license-keys/activate", null, null, "application/json", Http.ToJson(body));
					}
					catch (ApiException ex2)
					{
						if (ex2.Status == 403 || ex2.Status == 400)
						{
							throw new ApiException(-1, "esta licencia ya está activada en otro PC (cada licencia vale en un solo PC). Quítala de ese PC (Licencia → Quitar clave) o desde «Gestionar o cancelar mi suscripción».");
						}
						throw;
					}
					newAct = Http.Str(a, "id");
					info = PolarValidate(key, newAct);
				}
				if (info.Plan == Plan.None)
				{
					problem = info.Error;
					newCache = "";
				}
				else
				{
					newCache = (int)info.Plan + "|" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + info.Name.Replace("|", "") + "|" + (info.Gift ? "r" : "s");
				}
			}, delegate(Exception err)
			{
				licChecking = false;
				ApiException api = err as ApiException;
				if (err != null)
				{
					if (api != null && api.Status == 0)
					{
						// Sin internet: se sigue con lo último comprobado; se reintenta en 15 minutos.
						offline = true;
						licNextCheck = DateTime.UtcNow.AddMinutes(15.0);
						problem = "no se ha podido conectar con la tienda (" + err.Message + ")";
					}
					else if (api != null && api.Status == 404)
					{
						problem = "la clave ya no vale: has cambiado de plan, se ha cancelado la suscripción o no se ha podido cobrar";
						// Si antes valía, se avisa (una vez). Si se vuelve a cobrar, Polar reactiva la
						// MISMA clave: se comprueba cada hora para que vuelva a funcionar sola.
						if (licCache.Length > 0 && done == null)
						{
							licLost = true;
						}
						newCache = "";
						licNextCheck = DateTime.UtcNow.AddHours(1.0);
					}
					else
					{
						problem = Why(err);
						newCache = (api != null && api.Status == -1) ? "" : null;
					}
				}
				if (key != licKey)
				{
					return;
				}
				if (!offline)
				{
					licActivation = newAct;
				}
				if (newCache != null)
				{
					licCache = newCache;
				}
				SaveConfig();
				ComputePlan(!engine.OutputActive);
				if (problem != null && done == null)
				{
					AppendLog("Licencia: " + problem + ".");
				}
				if (done != null)
				{
					done(problem);
				}
			});
		}

		private static LicenseInfo PolarValidate(string key, string activation)
		{
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{ "key", key },
				{ "organization_id", License.PolarOrg }
			};
			if (activation.Length > 0)
			{
				body["activation_id"] = activation;
				// La activación es de este PC: con la huella de otro equipo, Polar dice que no existe.
				body["conditions"] = MachineConditions();
			}
			Dictionary<string, object> r = Http.Send("POST", License.PolarApi + "/v1/customer-portal/license-keys/validate", null, null, "application/json", Http.ToJson(body));
			return License.FromPolar(r, DateTime.UtcNow);
		}

		// Al quitar una clave de Polar se libera este PC (si falla, no pasa nada).
		private static void PolarDeactivate(string key, string activation)
		{
			if (activation.Length == 0 || !License.PolarReady)
			{
				return;
			}
			System.Threading.ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					Dictionary<string, object> body = new Dictionary<string, object>
					{
						{ "key", key },
						{ "organization_id", License.PolarOrg },
						{ "activation_id", activation }
					};
					Http.Send("POST", License.PolarApi + "/v1/customer-portal/license-keys/deactivate", null, null, "application/json", Http.ToJson(body));
				}
				catch
				{
				}
			});
		}

		// ---------- Qué deja hacer cada plan ----------

		// Chat, Overlays y Mando son del plan Completo (y los clips: ClipsAllowed). El Básico tiene
		// Directo, Ajustes, Título y Registro.
		private bool PageAllowed(int p)
		{
			if (plan == Plan.Full)
			{
				return true;
			}
			return p != PageChat && p != PageOverlays && p != PageMando && p != PageClips;
		}

		// Clips (botón Clip, Ctrl+Alt+C, !guardar y el Mando): plan Completo.
		private bool ClipsAllowed()
		{
			return plan == Plan.Full;
		}

		// Para Iniciar hace falta algún plan (prueba, Básico o Completo).
		private bool CanStream()
		{
			if (plan != Plan.None)
			{
				return true;
			}
			I18n.Show(this, "No hay ningún plan activo" + (planInfo.Error.Length > 0 ? ": " + planInfo.Error : "") + ".\n\nCon el plan Básico (" + License.PriceBasic + ") tienes el delay dinámico, el multistream y el título. Con el Completo (" + License.PriceFull + "), todo.", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Information);
			LicenseDialog();
			return plan != Plan.None;
		}

		// ---------- Botón de arriba y aviso de las pestañas ----------

		private void BuildLicense(int pageTop)
		{
			btnPlan = new VButton
			{
				Kind = ButtonKind.Link,
				Text = PlanButtonText(),
				FontSize = 8f
			};
			Place(header, btnPlan, 196f, 38f, 110f, 20f);
			btnPlan.Click += delegate
			{
				LicenseDialog();
			};
			lockBox = Box(this, 20f, pageTop + 16, 420f, 330f, PaintLocked, PageLocked);
			VButton see = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Ver planes y activar",
				FontSize = 10f
			};
			// Dentro del recuadro (si fuera aparte, quedaría tapado por él).
			Place(lockBox, see, 40f, 254f, 340f, 44f);
			see.Click += delegate
			{
				LicenseDialog();
			};
		}

		private void PaintLocked(Graphics g, Rectangle r)
		{
			Card.Glow(g, new RectangleF(r.Width * 0.1f, -r.Height * 0.2f, r.Width * 0.8f, r.Height * 0.8f), Color.FromArgb(40, Theme.Violet));
			string tab = (lockedPage == PageChat) ? "Chat" : ((lockedPage == PageOverlays) ? "Overlays" : ((lockedPage == PageMando) ? "Mando" : ((lockedPage == PageClips) ? "Clips" : "Título")));
			string what;
			switch (lockedPage)
			{
			case PageChat:
				what = "El chat conjunto de Twitch, Kick, YouTube y TikTok en una sola ventana y en OBS, con moderación, comandos (!delay, !clip…), follows, donaciones y estilos para la pantalla.";
				break;
			case PageOverlays:
				what = "Overlays para OBS: chat en pantalla, espectadores, avisos de subs y donaciones, y su estilo.";
				break;
			case PageMando:
				what = "Controla el delay desde el móvil, el Stream Deck o un panel de OBS.";
				break;
			case PageClips:
				what = "Guarda los últimos 60 s del directo con un botón, Ctrl+Alt+C o !guardar en el chat, y gestiona tus clips desde aquí.";
				break;
			default:
				what = "Cambia el título y la categoría en Twitch, Kick y YouTube a la vez, y conecta tus cuentas para escribir y moderar en el chat.";
				break;
			}
			Theme.DrawText(g, "PLAN COMPLETO", Theme.Mono(8f), Theme.Violet, new RectangleF(0f, S(40f), r.Width, S(20f)), StringAlignment.Center, StringAlignment.Center);
			Theme.DrawText(g, tab, Theme.Display(20f), Theme.Text, new RectangleF(0f, S(64f), r.Width, S(40f)), StringAlignment.Center, StringAlignment.Center);
			Theme.DrawWrapped(g, what, Theme.Body(9.5f), Theme.Muted, new RectangleF(S(40f), S(118f), r.Width - S(80f), S(70f)));
			string now = (plan == Plan.Basic) ? "Tienes el plan Básico (delay dinámico, multistream y título)." : ("No hay ningún plan activo" + (planInfo.Error.Length > 0 ? ": " + planInfo.Error : "") + ".");
			Theme.DrawWrapped(g, now + " El plan Completo cuesta " + License.PriceFull + " y se cancela cuando quieras.", Theme.Body(8.5f), Theme.Dim, new RectangleF(S(40f), S(196f), r.Width - S(80f), S(50f)));
		}

		// ---------- Ventana "Licencia" ----------

		private void LicenseDialog()
		{
			Form f = DarkForm("Licencia", 460, 596);
			PaintBox status = Box(f, 20f, 16f, 420f, 92f, delegate(Graphics g, Rectangle r)
			{
				Card.Glow(g, new RectangleF(-r.Width * 0.2f, -r.Height, r.Width * 0.9f, r.Height * 2.5f), Color.FromArgb(36, (plan == Plan.None) ? Theme.Yellow : Theme.Violet));
				Theme.DrawText(g, "TU PLAN", Theme.Mono(7f), Theme.Muted, new RectangleF(0f, S(4f), r.Width, S(16f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, (plan == Plan.None) ? "Sin plan" : License.PlanName(plan), Theme.Display(18f), (plan == Plan.None) ? Theme.Yellow : Theme.Text, new RectangleF(0f, S(20f), r.Width, S(34f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawWrapped(g, StatusLine(), Theme.Body(8.5f), Theme.Muted, new RectangleF(0f, S(56f), r.Width, S(36f)));
			});

			Card plans = new Card
			{
				Title = "Planes",
				Subtitle = "Pago mes a mes. Se cancela cuando quieras."
			};
			Place(f, plans, 20f, 116f, 420f, 220f);
			PlanRow(plans, 54f, "Básico", License.PriceBasic, "Delay dinámico, multistream (Twitch, Kick, YouTube, TikTok y otros a la vez), avisos del delay en el chat y título y categoría.", License.BuyBasicUrl, plan == Plan.Basic && planInfo.Source != "prueba");
			PlanRow(plans, 134f, "Completo", License.PriceFull, "Todo: además, clips de los últimos 60 s, chat conjunto con moderación, overlays y Mando (móvil y Stream Deck).", License.BuyFullUrl, plan == Plan.Full && planInfo.Source != "prueba");

			Card act = new Card
			{
				Title = "Activar una clave",
				Subtitle = "La de tu compra o la que te hayan regalado."
			};
			Place(f, act, 20f, 346f, 420f, 182f);
			Field fKey = new Field();
			fKey.Box.Multiline = true;
			fKey.Box.ScrollBars = ScrollBars.None;
			fKey.Box.Text = licKey;
			Place(act, fKey, 20f, 52f, 380f, 60f);
			VButton btnAct = new VButton { Kind = ButtonKind.Primary, Text = "Activar", FontSize = 10f };
			VButton btnDel = new VButton { Kind = ButtonKind.Ghost, Text = "Quitar clave", FontSize = 9f };
			Place(act, btnAct, 20f, 122f, 250f, 42f);
			Place(act, btnDel, 280f, 122f, 120f, 42f);
			btnDel.Muted = licKey.Length == 0;

			VButton manage = new VButton { Kind = ButtonKind.Link, Text = "Gestionar o cancelar mi suscripción", FontSize = 8.5f };
			Place(f, manage, 20f, 534f, 260f, 22f);
			manage.Click += delegate
			{
				Http.OpenBrowser(License.ManageUrl);
			};
			VButton close = new VButton { Kind = ButtonKind.Ghost, Text = "Cerrar", FontSize = 10f };
			Place(f, close, 320f, 538f, 120f, 42f);
			close.Click += delegate
			{
				f.Close();
			};

			btnAct.Click += delegate
			{
				string key = fKey.Box.Text.Trim();
				if (key.Length == 0)
				{
					I18n.Show(f, "Pega primero la clave.", "Licencia");
					return;
				}
				if (License.IsSigned(key))
				{
					LicenseInfo info = License.CheckSigned(key, DateTime.UtcNow);
					if (info.Plan == Plan.None)
					{
						I18n.Show(f, "Esa clave no se puede usar: " + info.Error + ".", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						return;
					}
					SetLicenseKey(key);
					status.Invalidate();
					I18n.Show(f, "Listo: plan " + License.PlanName(info.Plan) + ((info.Name.Length > 0) ? (" a nombre de " + info.Name) : "") + ((info.Expires == DateTime.MaxValue) ? ", para siempre." : (", hasta el " + info.Expires.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture) + ".")), "Licencia");
					return;
				}
				if (!License.PolarReady)
				{
					I18n.Show(f, "Esa clave no es de Vortexs Live. Las claves empiezan por SD1-: cópiala entera, sin espacios.", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return;
				}
				string oldKey = licKey;
				string oldAct = licActivation;
				string oldCache = licCache;
				SetLicenseKey(key);
				btnAct.Text = "Comprobando…";
				btnAct.Enabled = false;
				CheckPolar(delegate(string problem)
				{
					if (f.IsDisposed)
					{
						return;
					}
					btnAct.Text = "Activar";
					btnAct.Enabled = true;
					status.Invalidate();
					if (problem == null)
					{
						I18n.Show(f, "Listo: plan " + License.PlanName(plan) + " activado.", "Licencia");
						return;
					}
					// No vale: se vuelve a la clave de antes.
					licKey = oldKey;
					licActivation = oldAct;
					licCache = oldCache;
					SaveConfig();
					ComputePlan(true);
					status.Invalidate();
					I18n.Show(f, "No se ha podido activar: " + problem + ".", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				});
			};
			btnDel.Click += delegate
			{
				if (licKey.Length == 0)
				{
					return;
				}
				if (I18n.Show(f, "¿Quitar la clave de este PC? Podrás volver a pegarla (aquí o en otro PC).", "Licencia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				{
					return;
				}
				if (!License.IsSigned(licKey))
				{
					PolarDeactivate(licKey, licActivation);
				}
				licKey = "";
				licActivation = "";
				licCache = "";
				fKey.Box.Text = "";
				btnDel.Muted = true;
				btnDel.Invalidate();
				SaveConfig();
				ComputePlan(!engine.OutputActive);
				status.Invalidate();
			};
			f.ShowDialog(this);
			f.Dispose();
		}

		// ---------- Activar desde la página de compra ----------
		// Al pagar, Polar lleva al navegador a vortexs.es/vortexs-live/activar, que enseña la clave y un
		// botón "Activar en Vortexs Live": abre http://127.0.0.1:8935/licencia?key=… y aquí se activa
		// sola, sin copiar nada. Lo llama el servidor web (otro hilo).
		private string LicenseFromWeb(string key)
		{
			if (key.Length < 8 || key.Length > 400)
			{
				return LicensePage(false, "No ha llegado ninguna clave. Vuelve a la página de la compra y pulsa otra vez «Activar en Vortexs Live».");
			}
			string problem = null;
			Plan got = Plan.None;
			System.Threading.ManualResetEvent ready = new System.Threading.ManualResetEvent(false);
			try
			{
				BeginInvoke((Action)delegate
				{
					if (License.IsSigned(key))
					{
						LicenseInfo info = License.CheckSigned(key, DateTime.UtcNow);
						if (info.Plan == Plan.None)
						{
							problem = info.Error;
						}
						else
						{
							SetLicenseKey(key);
							got = plan;
						}
						ready.Set();
						return;
					}
					if (!License.PolarReady)
					{
						problem = "esa clave no es de Vortexs Live";
						ready.Set();
						return;
					}
					string oldKey = licKey;
					string oldAct = licActivation;
					string oldCache = licCache;
					// Si ya hay una licencia de pago, se pregunta antes de cambiarla (así una página
					// cualquiera no puede cambiarla sin que lo sepas).
					if (key != licKey && licKey.Length > 0 && plan != Plan.None && planInfo.Source != "prueba")
					{
						if (I18n.Show(this, "La página de compra quiere activar otra licencia en este PC en lugar de la que tienes ahora. ¿Cambiarla?", "Licencia", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
						{
							problem = "has decidido mantener la licencia que ya tenías";
							ready.Set();
							return;
						}
					}
					// La misma clave que ya está aquí: se comprueba sin volver a activarla.
					if (key != licKey)
					{
						licKey = key;
						licActivation = "";
						licCache = "";
						SaveConfig();
					}
					CheckPolar(delegate(string p)
					{
						if (p != null && key != oldKey)
						{
							licKey = oldKey;
							licActivation = oldAct;
							licCache = oldCache;
							SaveConfig();
							ComputePlan(true);
						}
						else if (p == null && key != oldKey && oldKey.Length > 0 && !License.IsSigned(oldKey))
						{
							// La nueva vale: se libera este PC en la licencia anterior.
							PolarDeactivate(oldKey, oldAct);
						}
						problem = p;
						got = plan;
						ready.Set();
					});
				});
			}
			catch
			{
				return LicensePage(false, "Vortexs Live se está cerrando. Ábrelo y vuelve a pulsar «Activar».");
			}
			if (!ready.WaitOne(40000))
			{
				return LicensePage(false, "No se ha podido comprobar la clave a tiempo (¿hay internet?). Vuelve a pulsar «Activar».");
			}
			if (problem != null)
			{
				LogLater("Licencia: no se ha podido activar desde la web: " + problem + ".");
				return LicensePage(false, "No se ha podido activar: " + problem + ".");
			}
			try
			{
				BeginInvoke((Action)delegate
				{
					if (base.WindowState == FormWindowState.Minimized)
					{
						base.WindowState = FormWindowState.Normal;
					}
					Activate();
					AppendLog("Licencia: plan " + License.PlanName(got) + " activado desde la página de compra.");
				});
			}
			catch
			{
			}
			return LicensePage(true, "Plan " + License.PlanName(got) + " activado en este PC. Ya puedes cerrar esta pestaña y volver a Vortexs Live.");
		}

		private static string LicensePage(bool ok, string msg)
		{
			string title = ok ? "¡Listo!" : "No se ha podido activar";
			string color = ok ? "#34D399" : "#FFB3BA";
			return "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Vortexs Live</title>" +
				"<body style=\"margin:0;min-height:100vh;display:grid;place-items:center;background:#09090C;color:#F4F4F5;font:17px/1.55 'Segoe UI',system-ui,sans-serif\">" +
				"<div style=\"max-width:520px;padding:32px;text-align:center\"><div style=\"font-size:30px;font-weight:800;margin-bottom:6px\">Vortexs <span style=\"color:#C09AFD\">Live</span></div>" +
				"<div style=\"font-size:22px;font-weight:700;color:" + color + ";margin:18px 0 10px\">" + WebUtility.HtmlEncode(I18n.Tr(title)) + "</div>" +
				"<div style=\"color:#A1A1AA\">" + WebUtility.HtmlEncode(I18n.Tr(msg)) + "</div></div>";
		}

		private void SetLicenseKey(string key)
		{
			if (key != licKey && !License.IsSigned(licKey))
			{
				PolarDeactivate(licKey, licActivation);
			}
			licKey = key;
			licActivation = "";
			licCache = "";
			SaveConfig();
			ComputePlan(true);
		}

		private string StatusLine()
		{
			DateTime now = DateTime.UtcNow;
			string s;
			if (plan == Plan.None)
			{
				s = "No hay ningún plan activo" + ((planInfo.Error.Length > 0) ? (": " + planInfo.Error) : "") + ". Elige uno abajo o pega una clave.";
			}
			else if (planInfo.Source == "prueba")
			{
				s = "Prueba gratis con todo: queda " + License.Left(planInfo.Expires, now) + ".";
			}
			else if (planInfo.Source == "clave")
			{
				s = "Clave" + ((planInfo.Name.Length > 0) ? (" a nombre de " + planInfo.Name) : "") + ((planInfo.Expires == DateTime.MaxValue) ? ", para siempre." : (", hasta el " + planInfo.Expires.ToLocalTime().ToString("d/M/yyyy", CultureInfo.InvariantCulture) + "."));
			}
			else
			{
				s = (planInfo.Gift ? "Licencia de regalo, para siempre" : "Suscripción mensual") + ((planInfo.Name.Length > 0) ? (" (" + planInfo.Name + ")") : "") + ". Activada en este PC.";
			}
			if (licProblem.Length > 0 && planInfo.Source == "prueba")
			{
				s += " La clave guardada no vale: " + licProblem + ".";
			}
			return s;
		}

		private void PlanRow(Control parent, float y, string name, string price, string what, string url, bool current)
		{
			Box(parent, 20f, y, 270f, 72f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, name, Theme.BodyBold(11f), Theme.Text, new RectangleF(0f, 0f, r.Width, S(22f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, price, Theme.Mono(8f), Theme.Violet, new RectangleF(S(90f), 0f, r.Width - S(90f), S(22f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawWrapped(g, what, Theme.Body(8f), Theme.Muted, new RectangleF(0f, S(24f), r.Width, S(48f)));
			});
			VButton buy = new VButton
			{
				Kind = current ? ButtonKind.Ghost : ButtonKind.Primary,
				Text = current ? "Tu plan" : "Suscribirme",
				FontSize = 9f
			};
			Place(parent, buy, 300f, y + 10f, 100f, 38f);
			buy.Click += delegate
			{
				Http.OpenBrowser((url.Length > 0) ? url : License.PricesUrl);
				if (url.Length > 0)
				{
					AppendLog("Licencia: al terminar de pagar, pulsa «Activar en Vortexs Live» en la página que se abre y se activa sola (deja Vortexs Live abierto).");
				}
			};
		}
	}
}
