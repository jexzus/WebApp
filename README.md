# Delivery WebApp
📦 AppWeb1 — Sistema de Gestión de Pedidos y Delivery

Aplicación web desarrollada en ASP.NET Core MVC (.NET 8) para la gestión completa de un sistema de pedidos tipo delivery / e-commerce, con catálogo de productos, carrito, pedidos, asignación de repartidores, pagos con MercadoPago y notificaciones en tiempo real mediante SignalR.

El sistema contempla distintos roles de usuario, permisos diferenciados y un flujo completo desde la compra hasta la entrega.

🚀 Tecnologías utilizadas

ASP.NET Core MVC (.NET 8)

Entity Framework Core

SQL Server

SignalR (notificaciones en tiempo real)

Session Authentication

JWT Service (para integraciones/API)

MercadoPago SDK

BCrypt (hash de contraseñas)

EPPlus / PDFSharpCore / QuestPDF (exportaciones)

Bootstrap + JavaScript + AJAX

🧱 Arquitectura del proyecto
AppWeb1
│
├── Areas
│   ├── Admin
│   │   ├── Controllers
│   │   │   ├── CatalogoController
│   │   │   └── PedidosController
│   │   └── Views
│
│   └── Cliente
│       ├── Controllers
│       │   └── ClienteController
│       └── Views
│
├── Controllers
│   ├── UsuarioController
│   └── PagosController
│
├── Data
│   └── DeliveryDBContext
│
├── Hubs
│   └── DeliveryHub
│
├── Models
│   ├── Usuario
│   ├── Cliente
│   ├── Producto
│   ├── Pedido
│   └── DetallePedido
│
├── Security
│   └── Roles
│
├── Helpers
│   └── JwtService
│
└── wwwroot
    └── imagenes
👥 Actores del sistema

El sistema contempla 5 roles principales.

Rol	Descripción
Cliente	Compra productos, realiza pedidos y ve el estado de los mismos
Repartidor	Recibe alertas de pedidos disponibles y realiza entregas
Vendedor	Gestiona catálogo y pedidos
Admin	Gestiona pedidos, catálogo y personal
SuperAdmin	Control total del sistema
🔐 Sistema de autenticación

El sistema utiliza Session Authentication.

Cuando un usuario inicia sesión se guardan en sesión:

Session["Usuario"]
Session["Rol"]

Esto permite controlar:

acceso a páginas

permisos

redirecciones automáticas

acciones permitidas

🔑 Permisos por rol
Acción	Cliente	Repartidor	Vendedor	Admin	SuperAdmin
Ver catálogo	✔	❌	✔	✔	✔
Comprar productos	✔	❌	❌	❌	❌
Ver pedidos	✔ (propios)	✔ (asignados)	✔	✔	✔
Cambiar estado pedido	❌	✔ (entregado)	✔	✔	✔
Asignar repartidor	❌	❌	✔	✔	✔
Gestionar catálogo	❌	❌	✔	✔	✔
Crear usuarios staff	❌	❌	❌	✔	✔
Crear admins	❌	❌	❌	❌	✔
🛒 Flujo completo del sistema
1️⃣ Cliente

Se registra o inicia sesión

Accede al catálogo

Agrega productos al carrito

Confirma pedido

Selecciona método de pago

El pedido pasa a Pendiente

2️⃣ Preparación del pedido

Staff cambia estados:

Pendiente
↓
En preparación
3️⃣ Tipo de entrega
Retiro en local
En preparación
↓
Para retirar
↓
Entregado
Delivery
En preparación
↓
Esperando repartidor
↓
En reparto
↓
Entregado
🛵 Sistema de reparto en tiempo real

Se utiliza SignalR.

Hub principal:

/deliveryHub

Grupos de usuarios:

Admins
Repartidores
Cliente_{id}

Eventos enviados:

Evento	Descripción
NuevoPedidoParaReparto	alerta a repartidores
PedidoAceptado	repartidor toma pedido
PedidoCancelado	pedido cancelado
MiPedidoActualizado	cliente recibe actualización
EstadoCambiadoGlobal	todos reciben actualización
💳 Pagos con MercadoPago

El sistema utiliza dos mecanismos:

1️⃣ Retorno visual
/Pagos/Retorno

Estados posibles:

success
pending
failure

Si el pago es exitoso se limpia el carrito.

2️⃣ Webhook (fuente de verdad)
POST /Pagos/Webhook

MercadoPago notifica cuando:

pago aprobado

pago rechazado

pago pendiente

Esto permite actualizar el pedido en el sistema.

🖼 Manejo de imágenes

Las imágenes no se guardan en base de datos.

Se almacenan en:

wwwroot/imagenes/

Formato:

GUID.extension

Ejemplo:

8c1fbb2a-4a1a-4a9c-95f2.jpg

Restricciones:

formatos permitidos

jpg
jpeg
png
gif
webp

tamaño máximo

5MB
🔔 Sistema de notificaciones

Las notificaciones del sistema utilizan:

SignalR

AJAX

JavaScript

Esto permite:

cambios de estado en vivo

alertas de repartidores

actualización automática de pedidos

notificaciones en el panel admin

📊 Exportación de datos

El sistema permite exportar:

Pedidos en Excel

Pedidos en PDF

Utilizando:

EPPlus
PDFSharpCore
QuestPDF
⚙️ Configuración necesaria

Archivo:

appsettings.json

Ejemplo:

"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=DeliveryDB;Trusted_Connection=True;"
},

"MercadoPago": {
  "AccessToken": "TU_TOKEN",
  "BasePublicUrl": "https://tu-dominio.com"
}
▶️ Cómo ejecutar el proyecto
1️⃣ Clonar repositorio
git clone https://github.com/usuario/AppWeb1.git
2️⃣ Restaurar paquetes
dotnet restore
3️⃣ Migraciones
dotnet ef database update
4️⃣ Ejecutar proyecto
dotnet run
🧠 Reglas importantes del sistema

1️⃣ La sesión determina el rol activo

2️⃣ Los estados del pedido tienen transiciones válidas

3️⃣ El webhook de MercadoPago es la fuente real del pago

4️⃣ SignalR mantiene sincronizados clientes, admins y repartidores

5️⃣ Las imágenes siempre se guardan en disco, nunca en base de datos

📌 Futuras mejoras

API REST completa

autenticación JWT completa

app móvil MAUI / Blazor Hybrid

panel analytics

geolocalización repartidores

tracking del pedido en mapa

sistema de stock

👨‍💻 Autor

Proyecto desarrollado por:

Jesus Raiburn

Sistema desarrollado como plataforma completa de gestión de pedidos, delivery y comercio electrónico.
