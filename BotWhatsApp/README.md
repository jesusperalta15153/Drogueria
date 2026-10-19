# 🤖 Bot de WhatsApp para FarmaVida

Bot interactivo de WhatsApp conectado directamente a la base de datos MySQL de FarmaVida y al servidor ASP.NET para brindar soporte técnico, resolver problemas con la página web, rastrear pedidos y consultar productos en tiempo real.

---

## 🚀 ¿Cómo iniciarlo?

1. Asegúrate de tener **XAMPP / MySQL** encendido con la base de datos `Drogueria`.
2. Haz doble clic en el archivo:
   `iniciar-bot.bat`
   (o en terminal: `cd C:\Users\jesus\Desktop\AAA\BotWhatsApp` y luego `node index.js`).
3. Aparecerá un código QR en la consola de comandos.
4. Abre WhatsApp en tu celular (el número de la droguería `+57 313 524 8994` o cualquier número de atención):
   - Ve a **Ajustes / Configuración** > **Dispositivos vinculados**.
   - Selecciona **Vincular un dispositivo** y escanea el código QR de la pantalla.
5. ¡Listo! El bot quedará conectado y responderá automáticamente.

---

## 🛠️ ¿Qué hace el Bot automáticamente?

1. **Cuando el cliente viene desde la página web (Botón de Ayuda con el Pedido):**
   - El cliente envía: `"Hola FarmaVida, necesito ayuda con mi pedido #xxxxxxx"`.
   - El bot consulta automáticamente en MySQL el estado del pedido, los medicamentos comprados, el total y la dirección de entrega, dándole una respuesta inmediata al usuario.
2. **Diagnóstico técnico y estado del sistema (`ESTADO` o `SISTEMA`):**
   - Comprueba en tiempo real si MySQL está respondiendo y si el servidor web ASP.NET está en línea.
3. **Solución a problemas comunes de la web (`PROBLEMAS` o `AYUDA WEB`):**
   - Asiste al usuario si no puede iniciar sesión, problemas al pagar o registrar direcciones.
4. **Consulta de medicamentos y precios (`BUSCAR [nombre]`):**
   - Consulta el inventario real y stock de la farmacia.
5. **Transferencia a Asesor Humano (`ASESOR`):**
   - Deja el mensaje listo para que un encargado humano intervenga.
