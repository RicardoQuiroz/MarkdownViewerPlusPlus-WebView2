# MarkdownViewer++ (WebView2) — novedades de la versión 0.9.0

Fork de [MarkdownViewer++](https://github.com/nea/MarkdownViewerPlusPlus) (Savas Ziplies) por **RicardoQuiroz** y **Claude (Anthropic)**. [English](README.md)

El motor HTML antiguo (HtmlRenderer) se reemplazó por **Microsoft Edge WebView2**, que ya viene con Windows 10/11.

| Antes | Ahora |
|---|---|
| No se podía seleccionar/copiar texto del visor | Selección normal + **Ctrl+C** o clic derecho → *Copiar* |
| Los enlaces internos movían solo el visor | Al hacer clic en un enlace `#sección` el **editor salta a la misma línea**. Los enlaces a otros `.md` (`otro.md#parte`) abren el archivo en Notepad++ en esa sección. Los enlaces web abren el navegador |
| Sin tema oscuro | Usa los **colores del tema de Notepad++** (Configurador de estilos / Modo oscuro) y cambia al instante. Se puede desactivar: *Plugins → MarkdownViewer++ (WebView2) → Follow Notepad++ theme* |
| No se actualizaba al editar/guardar (bug de 64 bits en las notificaciones de Scintilla) | Se actualiza **mientras escribes** (300 ms) y al **guardar** |
| Solo Ctrl+Shift+M para abrir | **Ctrl+Shift+M** abre y cierra (también con el foco dentro del visor). **Esc** dentro del visor lo cierra. **F5** refresca, **Ctrl+S** guarda el archivo |
| Emojis/iconos no se veían | Emojis a color, atajos `:smile:`, imágenes/SVG locales (rutas relativas, `../` y absolutas), alertas de GitHub `> [!NOTE]` con icono, casillas de tareas |
| Sin ventana independiente | Botón **Undock/Dock** en el visor (y en el menú del plugin): lo pasa a una ventana propia con botón en la barra de tareas, que se puede llevar a otro monitor. Recuerda posición y tamaño |

Además: desplazamiento sincronizado en **ambos sentidos** (editor ↔ visor) por línea de origen en lugar de por porcentaje; exportar a PDF usa el motor de Edge (mismo aspecto que el visor); imprimir abre el diálogo de impresión de Edge.

### Compilar e instalar

Requisitos: .NET SDK 8 (`winget install Microsoft.DotNet.SDK.8`) y el runtime WebView2 (incluido en Windows 11).

```powershell
dotnet build MarkdownViewerPlusPlus\MarkdownViewerPlusPlus.csproj -c Release -p:Platform=x64
```

El plugin queda en `dist\x64\MarkdownViewerPlusPlus\`. Para instalarlo, cierra Notepad++ y ejecuta:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

El script guarda la versión anterior en `plugins\disabled\MarkdownViewerPlusPlus.backup-<fecha>` y pide permisos de administrador si hacen falta. Tus opciones (`plugins\Config\MarkdownViewerPlusPlus.ini`) se conservan.

Para depurar: define la variable de entorno `MDV_DEBUG=1` antes de abrir Notepad++ y revisa `%TEMP%\MarkdownViewerPlusPlus.log`.

