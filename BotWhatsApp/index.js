require('dotenv').config();
const { default: makeWASocket, useMultiFileAuthState, DisconnectReason } = require('@whiskeysockets/baileys');
const QRCodeImage = require('qrcode');
const fs = require('fs');
const qrcode = require('qrcode-terminal');
const mysql = require('mysql2/promise');
const pino = require('pino');
const path = require('path');
const http = require('http');
const https = require('https');

// Configuración de Base de Datos (cargada desde .env o por defecto)
const dbConfig = {
    host: process.env.DB_HOST || 'localhost',
    port: parseInt(process.env.DB_PORT || '3306', 10),
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD !== undefined ? process.env.DB_PASSWORD : '',
    database: process.env.DB_NAME || 'Drogueria'
};

// URL del sistema FarmaVida para diagnóstico
const WEB_URL = (process.env.WEB_URL || 'https://drogueria-1ksj.onrender.com').replace(/\/+$/, '');
const BOT_PHONE_NUMBER = (process.env.BOT_PHONE_NUMBER || '573135958228').replace(/\D/g, '');
// Leer modo de vinculación desde argumentos de consola
const args = process.argv.slice(2);
const useCode = args.includes('--code') || args.includes('--method=code');
const isQRMode = !useCode;


async function getDbConnection() {
    return await mysql.createConnection(dbConfig);
}

// Función auxiliar para consumir APIs REST en Render (soporta https y http)
function fetchJson(url) {
    return new Promise((resolve, reject) => {
        const client = url.startsWith('https') ? https : http;
        const req = client.get(url, { timeout: 10000 }, (res) => {
            if (res.statusCode < 200 || res.statusCode >= 300) {
                return reject(new Error(`HTTP ${res.statusCode}`));
            }
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                try {
                    resolve(JSON.parse(data));
                } catch (e) {
                    reject(e);
                }
            });
        });
        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('Timeout de conexión')); });
    });
}

// Función de diagnóstico del sistema
async function verificarEstadoSistema() {
    let dbOk = false;
    let webOk = false;
    let detalles = [];

    // 1. Probar mediante la API de diagnóstico en Render
    try {
        const diag = await fetchJson(`${WEB_URL}/api/bot/diagnostico`);
        if (diag && diag.success) {
            webOk = true;
            dbOk = diag.dbConectada;
            detalles.push(`✅ *Servidor Web ASP.NET:* En línea (${WEB_URL})`);
            detalles.push(`✅ *Base de Datos:* Conectada. Total pedidos: ${diag.totalPedidos} | Total medicamentos: ${diag.totalProductos}`);
            return {
                estadoGeneral: 'OPERATIVO',
                detalles: detalles.join('\n')
            };
        }
    } catch (apiErr) {
        console.log('Aviso API diagnóstico:', apiErr.message);
    }

    // Fallback: Probar servidor Web
    try {
        await new Promise((resolve, reject) => {
            const client = WEB_URL.startsWith('https') ? https : http;
            const req = client.get(WEB_URL, { timeout: 6000 }, (res) => {
                if (res.statusCode >= 200 && res.statusCode < 400) {
                    webOk = true;
                    resolve(true);
                } else {
                    reject(new Error(`Código HTTP ${res.statusCode}`));
                }
            });
            req.on('error', reject);
            req.on('timeout', () => { req.destroy(); reject(new Error('Timeout')); });
        });
        detalles.push(`✅ *Servidor Web ASP.NET:* En línea (${WEB_URL})`);
    } catch (err) {
        webOk = false;
        detalles.push(`⚠️ *Servidor Web:* Inaccesible o detenido (${err.message})`);
    }

    // Fallback: Probar base de datos MySQL local
    try {
        const conn = await getDbConnection();
        const [rows] = await conn.execute('SELECT COUNT(*) as total FROM Pedidos');
        dbOk = true;
        detalles.push(`✅ *Base de Datos (MySQL local):* Conectada. Total pedidos: ${rows[0].total}`);
        await conn.end();
    } catch (err) {
        dbOk = false;
        detalles.push(`❌ *Base de Datos:* ERROR (${err.message})`);
    }

    return {
        estadoGeneral: dbOk && webOk ? 'OPERATIVO' : 'INCIDENTES DETECTADOS',
        detalles: detalles.join('\n')
    };
}

// Buscar pedido por ID corto o UUID
async function consultarPedido(codigoPedido) {
    const search = codigoPedido.trim().replace('#', '');

    // 1. Intentar primero a través de la API en Render (conecta con la base de datos real del hosting)
    try {
        const resApi = await fetchJson(`${WEB_URL}/api/bot/pedido/${encodeURIComponent(search)}`);
        if (resApi && resApi.success && resApi.pedido) {
            return {
                pedido: {
                    Id: resApi.pedido.id,
                    Nombre: resApi.pedido.nombre,
                    Apellido: resApi.pedido.apellido,
                    Email: resApi.pedido.email,
                    Fecha: resApi.pedido.fecha,
                    Estado: resApi.pedido.estado,
                    DireccionEntrega: resApi.pedido.direccionEntrega,
                    Total: resApi.pedido.total,
                    Repartidor: resApi.pedido.repartidor,
                    CodigoConfirmacion: resApi.pedido.codigoConfirmacion
                },
                detalles: (resApi.detalles || []).map(d => ({
                    Cantidad: d.cantidad,
                    PrecioUnitario: d.precioUnitario,
                    Subtotal: d.subtotal,
                    Nombre: d.nombre
                }))
            };
        } else if (resApi && resApi.message && !resApi.success) {
            return null;
        }
    } catch (apiErr) {
        console.log('Consulta API falló, probando conexión directa MySQL:', apiErr.message);
    }

    // 2. Fallback a consulta directa MySQL
    let conn;
    try {
        conn = await getDbConnection();
        const [pedidos] = await conn.execute(
            `SELECT p.Id, p.Fecha, p.Total, p.Estado, p.DireccionEntrega, u.Nombre, u.Apellido, u.Email 
             FROM Pedidos p
             LEFT JOIN Usuarios u ON p.UsuarioId = u.Uuid
             WHERE p.Id LIKE ? OR SUBSTRING(p.Id, 1, 8) = ?
             ORDER BY p.Fecha DESC LIMIT 1`,
            [`%${search}%`, search]
        );

        if (pedidos.length === 0) {
            return null;
        }

        const ped = pedidos[0];
        const [detalles] = await conn.execute(
            `SELECT d.Cantidad, d.PrecioUnitario, d.Subtotal, pr.Nombre 
             FROM DetallesPedido d 
             LEFT JOIN Productos pr ON d.ProductoId = pr.Id 
             WHERE d.PedidoId = ?`,
            [ped.Id]
        );

        return { pedido: ped, detalles };
    } catch (err) {
        console.error('Error consultando pedido directo:', err);
        return { error: err.message };
    } finally {
        if (conn) await conn.end();
    }
}

// Buscar medicamentos / productos
async function buscarMedicamento(termino) {
    // 1. Intentar primero a través de la API en Render
    try {
        const resApi = await fetchJson(`${WEB_URL}/api/bot/productos?q=${encodeURIComponent(termino)}`);
        if (resApi && resApi.success && Array.isArray(resApi.productos)) {
            return resApi.productos.map(p => ({
                Nombre: p.nombre,
                Precio: p.precio,
                Stock: p.stock,
                Descripcion: p.descripcion,
                Estado: 1
            }));
        }
    } catch (apiErr) {
        console.log('Búsqueda API falló, probando conexión directa MySQL:', apiErr.message);
    }

    // 2. Fallback a consulta directa MySQL
    let conn;
    try {
        conn = await getDbConnection();
        const [prods] = await conn.execute(
            `SELECT Nombre, Precio, Stock, Descripcion, Estado 
             FROM Productos 
             WHERE Nombre LIKE ? AND Estado = 1 
             LIMIT 5`,
            [`%${termino}%`]
        );
        return prods;
    } catch (err) {
        console.error('Error buscando producto directo:', err);
        return [];
    } finally {
        if (conn) await conn.end();
    }
}

async function startBot() {
    const { state, saveCreds } = await useMultiFileAuthState(path.join(__dirname, 'auth_info_baileys'));

    const sock = makeWASocket({
        auth: state,
        logger: pino({ level: 'silent' }),
        printQRInTerminal: false
    });

    // Si se eligió vincular por código de teléfono:
    let pairingRequested = false;
    if (useCode && BOT_PHONE_NUMBER && !sock.authState.creds.registered) {
        setTimeout(async () => {
            if (!sock.authState.creds.registered && !pairingRequested) {
                pairingRequested = true;
                try {
                    const code = await sock.requestPairingCode(BOT_PHONE_NUMBER);
                    const formatted = code ? code.match(/.{1,4}/g).join('-') : code;
                    console.log('\n======================================================');
                    console.log('📲 CÓDIGO DE VINCULACIÓN DIRECTA PARA: +' + BOT_PHONE_NUMBER);
                    console.log('👉 TU CÓDIGO DE EMPAREJAMIENTO:  [  ' + formatted + '  ]  👈');
                    console.log('======================================================');
                    console.log('Pasos en tu WhatsApp (+57 313 595 8228):');
                    console.log('1. Abre WhatsApp en tu celular');
                    console.log('2. Ve a Dispositivos vinculados -> Vincular un dispositivo');
                    console.log('3. Toca abajo: "Vincular con el número de teléfono"');
                    console.log('4. Ingresa el código: ' + formatted);
                    console.log('======================================================\n');
                } catch (err) {
                    console.log('Aviso al solicitar código:', err.message);
                }
            }
        }, 3500);
    }

    sock.ev.on('connection.update', async (update) => {
        const { connection, lastDisconnect, qr } = update;

        if (qr && !useCode) {
            console.log('\n======================================================');
            console.log('📱 ESCANEA ESTE CÓDIGO QR DESDE TU WHATSAPP (Dispositivos vinculados):');
            console.log('📸 Se guardó también como imagen en: codigo-qr.png');
            console.log('======================================================\n');
            qrcode.generate(qr, { small: true });

            // Guardar imagen PNG para que tu compañero te la pueda enviar fácilmente por chat
            try {
                const qrPath = path.join(__dirname, 'codigo-qr.png');
                await QRCodeImage.toFile(qrPath, qr, {
                    width: 400,
                    margin: 2
                });
                console.log(`📁 Imagen QR lista para enviar: ${qrPath}\n`);
            } catch (err) {
                console.error('Error guardando imagen QR:', err);
            }
        }

        if (connection === 'close') {
            const statusCode = (lastDisconnect?.error)?.output?.statusCode;
            const isLoggedOut = statusCode === DisconnectReason.loggedOut || statusCode === 401;
            console.log(`⚠️ Conexión cerrada (Código: ${statusCode || 'desconocido'}).`);

            try {
                sock.ev.removeAllListeners();
            } catch (e) {}

            if (isLoggedOut) {
                console.log('❌ Sesión no válida o cerrada. Limpiando credenciales para iniciar desde cero...');
                setTimeout(() => {
                    try {
                        const fs = require('fs');
                        const authFolder = path.join(__dirname, 'auth_info_baileys');
                        if (fs.existsSync(authFolder)) {
                            fs.rmSync(authFolder, { recursive: true, force: true });
                        }
                    } catch (e) {}
                    console.log('🔄 Reiniciando bot con sesión limpia...\n');
                    startBot();
                }, 1500);
            } else {
                console.log('🔄 Reconectando en 3 segundos...');
                setTimeout(() => {
                    startBot();
                }, 3000);
            }
        } else if (connection === 'open') {
            console.log('\n======================================================');
            console.log('🚀 ¡BOT DE WHATSAPP FARMAVIDA CONECTADO EXITOSAMENTE!');
            console.log('El bot ya está listo para responder mensajes y dar soporte.');
            console.log('======================================================\n');
        }
    });

    sock.ev.on('creds.update', saveCreds);

    // Manejo de mensajes entrantes
    sock.ev.on('messages.upsert', async (m) => {
        const msg = m.messages[0];
        if (!msg.message || msg.key.fromMe) return;

        const sender = msg.key.remoteJid;
        const textoOriginal = msg.message.conversation || 
                              msg.message.extendedTextMessage?.text || 
                              '';
        const texto = textoOriginal.trim().toLowerCase();

        console.log(`📩 Mensaje recibido de ${sender}: "${textoOriginal}"`);

        // Caso 1: Si el mensaje viene del botón de la página ("Hola FarmaVida, necesito ayuda con mi pedido #xxxx")
        if (texto.includes('necesito ayuda con mi pedido') || texto.includes('ayuda con mi pedido')) {
            const match = textoOriginal.match(/#([a-zA-Z0-9\-]+)/);
            const codigo = match ? match[1] : '';

            if (codigo) {
                await sock.sendMessage(sender, {
                    text: `👋 ¡Hola! He detectado que estás consultando por el pedido *#${codigo}*.\n\nDame un momento mientras busco la información en nuestro sistema... ⏳`
                });

                const resultado = await consultarPedido(codigo);
                if (resultado && resultado.pedido) {
                    const p = resultado.pedido;
                    let info = `📦 *ESTADO DEL PEDIDO #${p.Id.substring(0, 8).toUpperCase()}*\n`;
                    info += `━━━━━━━━━━━━━━━━━━━━━\n`;
                    info += `👤 *Cliente:* ${p.Nombre || ''} ${p.Apellido || ''}\n`;
                    info += `📅 *Fecha:* ${new Date(p.Fecha).toLocaleString('es-CO')}\n`;
                    info += `🚦 *Estado:* *${p.Estado}*\n`;
                    info += `📍 *Dirección:* ${p.DireccionEntrega || 'Entrega en farmacia'}\n`;
                    info += `💰 *Total:* $${Number(p.Total).toLocaleString('es-CO')}\n\n`;
                    
                    if (resultado.detalles && resultado.detalles.length > 0) {
                        info += `💊 *Medicamentos en tu pedido:*\n`;
                        resultado.detalles.forEach(d => {
                            info += ` • ${d.Cantidad}x ${d.Nombre} - $${Number(d.Subtotal).toLocaleString('es-CO')}\n`;
                        });
                        info += `\n`;
                    }

                    if (p.Estado === 'Pendiente') {
                        info += `ℹ️ Tu pedido está siendo preparado en farmacia. Te avisaremos cuando salga a ruta.`;
                    } else if (p.Estado === 'En Camino' || p.Estado === 'Asignado') {
                        info += `🛵 Tu pedido ya fue asignado a un repartidor y va en camino a tu dirección.`;
                    } else if (p.Estado === 'Entregado') {
                        info += `✅ Tu pedido figura como ENTREGADO. Si no lo has recibido, indícanos aquí mismo.`;
                    }

                    info += `\n\n¿Tienes algún problema con los medicamentos recibidos o requieres un asesor humano? Responde con *ASESOR*.`;
                    await sock.sendMessage(sender, { text: info });
                    return;
                } else {
                    await sock.sendMessage(sender, {
                        text: `⚠️ No encontramos el pedido *#${codigo}* en el sistema. Por favor verifica el número o escribe *ESTADO* para realizar un diagnóstico.`
                    });
                    return;
                }
            }
        }

        // Caso 2: Diagnóstico técnico / Estado del sistema
        if (texto === 'estado' || texto === 'diagnostico' || texto === 'sistema' || texto === 'soporte') {
            await sock.sendMessage(sender, { text: '🔍 *Realizando diagnóstico de FarmaVida y base de datos...*' });
            const diag = await verificarEstadoSistema();
            let respuesta = `📊 *REPORTE DE DIAGNÓSTICO TÉCNICO*\n`;
            respuesta += `━━━━━━━━━━━━━━━━━━━━━\n`;
            respuesta += `🚦 *Estado General:* ${diag.estadoGeneral}\n\n`;
            respuesta += `${diag.detalles}\n\n`;
            respuesta += `Si la web no carga, asegúrate de tener iniciado el proyecto ASP.NET y MySQL en XAMPP.`;
            await sock.sendMessage(sender, { text: respuesta });
            return;
        }

        // Caso 3: Buscar productos / medicamentos
        if (texto.startsWith('buscar ') || texto.startsWith('precio ') || texto.startsWith('stock ')) {
            const termino = texto.replace(/^(buscar|precio|stock)\s+/, '');
            const prods = await buscarMedicamento(termino);
            if (prods.length === 0) {
                await sock.sendMessage(sender, {
                    text: `🔍 No encontramos medicamentos disponibles con "${termino}". Escribe *MENU* para ver más opciones.`
                });
            } else {
                let r = `💊 *RESULTADOS DE MEDICAMENTOS ENCONTRADOS:*\n━━━━━━━━━━━━━━━━━━━━━\n`;
                prods.forEach(p => {
                    const st = p.Stock > 0 ? `Stock: ${p.Stock} disp.` : `⚠️ Agotado`;
                    r += `• *${p.Nombre}*\n  Precio: $${Number(p.Precio).toLocaleString('es-CO')} | ${st}\n\n`;
                });
                r += `Para comprar entra a: ${WEB_URL}/Tienda/Catalogo`;
                await sock.sendMessage(sender, { text: r });
            }
            return;
        }

        // Caso 4: Consultar pedido por código directo (ej. "pedido 1234abcd")
        if (texto.startsWith('pedido ') || texto.startsWith('rastrear ')) {
            const cod = texto.replace(/^(pedido|rastrear)\s+/, '');
            const res = await consultarPedido(cod);
            if (res && res.pedido) {
                const p = res.pedido;
                let info = `📦 *PEDIDO #${p.Id.substring(0, 8).toUpperCase()}*\n`;
                info += `Estado: *${p.Estado}*\nTotal: $${Number(p.Total).toLocaleString('es-CO')}\n`;
                info += `Dirección: ${p.DireccionEntrega || 'Entrega local'}\n`;
                await sock.sendMessage(sender, { text: info });
            } else {
                await sock.sendMessage(sender, { text: `❌ No se encontró ningún pedido con el identificador "${cod}".` });
            }
            return;
        }

        // Caso 5: Ayuda con la página web / Problemas frecuentes
        if (texto === 'problemas' || texto === 'ayuda web' || texto === 'errores' || texto === '2') {
            let ayuda = `🛠️ *SOLUCIÓN DE PROBLEMAS FRECUENTES EN LA PÁGINA:*\n`;
            ayuda += `━━━━━━━━━━━━━━━━━━━━━\n`;
            ayuda += `1️⃣ *¿No puedes iniciar sesión?*\n`;
            ayuda += `   • Revisa mayúsculas y minúsculas en tu correo.\n`;
            ayuda += `   • Puedes recuperar tu contraseña desde el botón "¿Olvidaste tu contraseña?".\n\n`;
            ayuda += `2️⃣ *¿No te deja finalizar pedido o pagar?*\n`;
            ayuda += `   • Asegúrate de tener al menos una dirección registrada en tu perfil.\n`;
            ayuda += `   • Verifica que haya stock disponible de todos los productos en el carrito.\n\n`;
            ayuda += `3️⃣ *¿La página muestra error o no carga?*\n`;
            ayuda += `   • Envía *ESTADO* para verificar si el servidor y la base de datos están activos.\n\n`;
            ayuda += `4️⃣ *Hablar con un asesor:*\n`;
            ayuda += `   • Escribe *ASESOR* para transferirte con atención al cliente.`;
            await sock.sendMessage(sender, { text: ayuda });
            return;
        }

        // Caso 6: Pedir Asesor Humano
        if (texto.includes('asesor') || texto.includes('humano') || texto.includes('persona')) {
            await sock.sendMessage(sender, {
                text: `👨‍⚕️ *Transferencia a Soporte Humano*\n\nHemos notificado al equipo de guardia de la droguería. Un farmacéutico o técnico te responderá por este mismo chat en breve. Por favor déjanos el detalle de tu solicitud o problema.`
            });
            return;
        }

        // Menú principal por defecto
        let menu = `🏥 *¡HOLA! BIENVENIDO AL ASISTENTE VIRTUAL DE DROGUERÍA FARMAVIDA*\n`;
        menu += `━━━━━━━━━━━━━━━━━━━━━━━━━━\n`;
        menu += `¿En qué te puedo colaborar hoy? Elige una opción:\n\n`;
        menu += `1️⃣ *Rastrear pedido:* Envía *PEDIDO [código]* (ej: _pedido e23a_)\n`;
        menu += `2️⃣ *Problemas con la página:* Envía *PROBLEMAS*\n`;
        menu += `3️⃣ *Consultar precios/stock:* Envía *BUSCAR [medicamento]* (ej: _buscar acetaminofen_)\n`;
        menu += `4️⃣ *Diagnóstico del sistema:* Envía *ESTADO*\n`;
        menu += `5️⃣ *Hablar con una persona:* Envía *ASESOR*\n`;
        menu += `━━━━━━━━━━━━━━━━━━━━━━━━━━\n`;
        menu += `🌐 Web Oficial: ${WEB_URL}`;

        await sock.sendMessage(sender, { text: menu });
    });
}

startBot().catch(err => {
    console.error('Error fatal al iniciar el bot:', err);
});
