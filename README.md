# Vortexs Live

**Delay dinámico y multistream para tus directos con OBS** · *Dynamic stream delay and multistreaming for OBS*

> Antes se llamaba **StreamDelay**. Si vienes de la 3.x, la actualización automática conserva tus ajustes.

OBS envía el directo a Vortexs Live, que lo retrasa los segundos que elijas y lo reenvía a Twitch, Kick, YouTube o cualquier servidor RTMP. El delay se puede cambiar, activar o quitar **en pleno directo**, sin cortar el stream.

**Web:** [manuelferjo.github.io/VortexsLive](https://manuelferjo.github.io/VortexsLive/) · **Descarga:** [última versión](https://github.com/manuelferjo/VortexsLive/releases/latest)

<p align="center">
  <img src="web/img/directo.png" width="300" alt="Pestaña Directo: delay de 30 segundos activado">
  <img src="web/img/chat.png" width="300" alt="Pestaña Chat: chat conjunto de Twitch, Kick y YouTube">
  <img src="web/img/mando.png" width="300" alt="Pestaña Mando: control desde el móvil, delays guardados y avisos">
</p>

## Precios

**3 días de prueba gratis con todo**, sin tarjeta (una prueba por PC; la primera vez hace falta internet). Después, mes a mes (se cancela cuando quieras):

| Plan | Precio | Qué incluye |
|---|---|---|
| **Básico** | 5 €/mes | Delay dinámico, multistream y título y categoría (pestañas Directo, Ajustes y Título), con avisos del delay en el chat |
| **Completo** | 8 €/mes | Todo: además clips de 60 s, chat conjunto con moderación, overlays y Mando |

La licencia se activa en el programa: botón del plan, arriba (debajo de «English»). Las versiones hasta la 3.5 eran gratuitas y siguen funcionando igual.

## Funciones

- **Delay dinámico**: elige los segundos y cámbialos en directo. Al subirlo se repiten los últimos segundos emitidos; al bajarlo, el stream salta hacia delante. Atajos de teclado globales. Guarda el delay real de cada momento en `%APPDATA%\VortexsLive\delay_log.csv` para que [Vortexs Clips](https://github.com/manuelferjo/VortexsClips) sincronice tus VODs al segundo.
- **Multistream**: Twitch, Kick, YouTube y otro destino a la vez, con el mismo delay. OBS codifica una sola vez.
- **Clips**: guarda los últimos 60 s que ha visto el público con el botón Clip, `Ctrl+Alt+C`, el Mando o `!guardar` en el chat (tú y, si quieres, tus moderadores o personas concretas como tu editor), en la carpeta que elijas (también una de Google Drive, OneDrive o Dropbox para compartirlos).
- **Varias calidades en Twitch** (Transmisión mejorada de OBS).
- **Chat conjunto** de Twitch, Kick, YouTube y TikTok (TikTok con una clave gratuita de [Euler Stream](https://www.eulerstream.com)) con emotes de 7TV, BetterTTV y FrankerFaceZ, avisos de subs, regalos, raids, bits, Super Chats, follows y donaciones (StreamElements y Streamlabs), espectadores de cada plataforma, escribir en todos los chats a la vez, moderar (borrar, expulsar, banear y deshacer, modo lento, borrar el chat) y moderación automática (palabras prohibidas, enlaces, spam, bots).
- **Mando** desde el móvil, el Stream Deck o un panel de OBS.
- **Overlays** para OBS: delay, espectadores y chat en pantalla, personalizables (el chat con su propio estilo: fuente, tamaño, colores, con o sin fondo…).
- **Título y categoría** en Twitch, Kick y YouTube a la vez.
- Ligero: el vídeo no se vuelve a codificar (casi no usa CPU).

## Descarga

Descarga la última versión en **[Releases](https://github.com/manuelferjo/VortexsLive/releases)**, descomprime el ZIP y abre `VortexsLive.exe`. La guía completa está en `GUIA.html`, dentro del ZIP.

Requisitos: Windows 10 u 11 y OBS Studio.

> **Aviso de Windows:** Vortexs Live todavía no tiene firma digital, así que la primera vez que lo abras Windows puede mostrar «Windows protegió su PC». Pulsa **Más información → Ejecutar de todas formas**. Solo pasa con la primera descarga: las actualizaciones automáticas no lo muestran. Más detalles en [Firma digital](#firma-digital).

## Compilar desde el código

No hace falta instalar nada: `build.bat` usa el compilador de C# (.NET Framework 4) que ya trae Windows.

```
build.bat
```

Crea `VortexsLive.exe` en la misma carpeta (con la misma prueba y licencia que el de la descarga). El código está en `src/` y los recursos (página del chat, overlays, fuentes, icono) en `res/`.

La ventana principal (`MainForm`) está repartida en un archivo por pestaña: `LiveTab.cs` (Directo), `ChatTab.cs`, `Extras.cs` (Overlays), `Remote.cs` (Mando), `TitleTab.cs`, `SettingsTab.cs`, `LicenseUi.cs` (plan y licencia, con las reglas en `License.cs`) y, en `MainForm.cs`, lo común (Registro, atajos y `VortexsLive.ini`). El resumen está al principio de `MainForm.cs`.

GitHub compila el programa y pasa las pruebas del chat con cada cambio (pestaña **Actions**). Para comprobar que un cambio de limpieza no altera nada, `pruebas/ILDiff.cs` compara dos `.exe` instrucción por instrucción (ver `pruebas/LEEME.txt`).

La web está en `web/` y se publica sola en GitHub Pages al subir cambios (`.github/workflows/web.yml`).

## Publicar una versión

1. Cambia `Version` en `src/MainForm.cs` (por ejemplo a `4.1`), añade las novedades al `LEEME.txt` y, en `src/Novedades.cs`, una entrada corta para el usuario arriba de `ReleaseNews` (es lo que verá en la ventana de novedades al abrir la versión nueva).
2. Sube los cambios a GitHub.
3. En **Releases → Draft a new release**, crea la etiqueta `v4.1` (la misma versión con una `v` delante) y pulsa **Publish release**.

GitHub compila, comprueba que la versión coincide con la etiqueta y **adjunta el ZIP de descarga a la Release** él solo.

## Firma digital

Vortexs Live **todavía no está firmado digitalmente**: los certificados de firma de código son de pago (se valorará más adelante).

Por eso Windows puede avisar la primera vez que abres el `.exe` descargado («Windows protegió su PC» → **Más información → Ejecutar de todas formas**). Para que puedas fiarte igualmente:

- Todo el código está en este repositorio, y el ZIP de cada [Release](https://github.com/manuelferjo/VortexsLive/releases) lo compila **GitHub Actions** a partir de él (puedes ver cada compilación en la pestaña **Actions**).
- Cada ZIP tiene su huella **SHA-256** en la página de la Release. El actualizador automático la comprueba antes de instalar nada.
- Si lo prefieres, puedes compilarlo tú mismo con `build.bat` (ver arriba).

*English:* Vortexs Live isn't code-signed yet, so Windows SmartScreen may warn the first time you run the downloaded `.exe` (click **More info → Run anyway**). Every release ZIP is built from this repository's source by GitHub Actions and has its SHA-256 shown on the release page; automatic updates don't show the warning.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Vortexs Live only connects to the streaming platforms and services that the user configures (Twitch, Kick, YouTube, TikTok via Euler Stream, StreamElements, Streamlabs, and emote providers 7TV, BetterTTV and FrankerFaceZ), to GitHub to check for updates, and, if the user activates a subscription licence key, to Polar (api.polar.sh) to check that the key is valid (the key and the computer name are sent). Keys and tokens are stored locally, encrypted with Windows DPAPI for the current Windows user; nothing is sent to the developer.

## English summary

Vortexs Live is a Windows tool for streamers (3-day free trial, then a Basic plan at €5/month or a Full plan at €8/month). OBS sends the stream to Vortexs Live, which delays it by a configurable number of seconds (changeable live, without restarting the stream) and forwards it to Twitch, Kick, YouTube or any RTMP server at the same time. It also includes (Full plan) a unified chat for Twitch, Kick, YouTube and TikTok with 7TV/BTTV/FFZ emotes, a phone remote control and OBS overlays. The user interface is in Spanish.

## Licencia

Programa de pago desde la 4.0: ver [LICENSE](LICENSE) (las versiones hasta la 3.5 se publicaron con licencia MIT). El código se publica para que se pueda revisar y compilar para uso propio. · Las fuentes incluidas (Outfit, Plus Jakarta Sans y JetBrains Mono) usan la licencia SIL Open Font License 1.1 (`res/OFL-*.txt`).
