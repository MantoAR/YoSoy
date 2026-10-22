# Ventana de 24 h de WhatsApp

Implementada en `feature/chats-ventana-24h`, sin cambios de esquema ni cambios en `whatsapp-service`.

## Regla y reloj

- `VentanaDeConversacion` recibe `TimeProvider` y compara la hora local de la clínica con `MensajeChat.FechaHora`, un DATETIME sin zona. A las 24 h exactas la ventana está cerrada.
- `UltimoEntrante` recorre entrantes de la conversación por `FechaHora DESC, IdMensaje DESC`, en páginas de 20 con OFFSET y sin forzar un índice. Devuelve el primero que no reconozca `ComandoWhatsApp.EsBaja`; incluye sus variantes como STOP y signos.
- No usa `GROUP BY Texto`. `Texto` es NOT NULL en el esquema y en la base de pruebas; aun así, la lectura usa `IsDBNull` y un entrante sin texto contaría como real. No se alteró la columna para probar NULL.
- Los segundos restantes quedan entre 0 y 86400, incluso si el último entrante tiene fecha futura. La antigüedad nunca es negativa.

## Envío y contrato HTTP

`ServicioMensajes.EnviarAsync` controla la ventana antes de insertar o llamar al canal para `Humano` y `Sistema` sin `IniciaElNegocio` (cerrar/reabrir). Humano no puede saltársela pasando esa opción. Bot conserva la regla previa de conversación iniciada.

El endpoint `/responder` consulta primero y también respeta un rechazo posterior del servicio si vence entre los dos controles. El resultado interno conserva la evaluación exacta que rechazó el envío.

Una ventana cerrada devuelve 409 con `codigo: ventana_24h_cerrada`, `motivo: vencida | nunca_escribio` y `mensaje` en español. `ultimoEntranteHaceSegundos` está presente solo cuando existe un entrante real. No queda fila fantasma ni se guarda actividad o auditoría de una respuesta rechazada.

Lista y detalle conservan sus campos y suman `ventana: { abierta, motivo, segundosRestantes }`. Motivo es nulo cuando está abierta. Los textos son `VentanaDeConversacion.MensajeVencida` y `MensajeNuncaEscribio`.

Cerrar/reabrir sigue cambiando el estado aunque el aviso no pueda salir. En modo Personal conserva su comportamiento de no mandar esos avisos.

## Avisos de turno

`NotificadorTurnos` puede iniciar un aviso fuera de la ventana, exclusivamente como `Sistema` con `IniciaElNegocio`. Solo sale con `ConConsentimiento` vigente; `SinDecision`, revocación o llave apagada quedan `Omitido`. La llave apagada sigue sin tocar `ConsentimientoWhatsApp`.

`Mensajeria:TopeAvisosPorNumeroPorDia` vale 3 por defecto y se consulta en cada envío. Cero omite todos. El conteo incluye salientes `Sistema` en `Enviado` o `Pendiente`, entre las 00:00 locales y las 00:00 del día siguiente, en todas las conversaciones del mismo número guardado. No cuentan Error, Bot, Humano ni entrantes.

La reserva del cupo y la inserción como Pendiente ocurren en una transacción, con `sp_getapplock` por número/día. El candado termina antes de llamar al canal; la fila Pendiente ya consume cupo y coordina otras instancias de la API. El exceso devuelve un resultado Omitido sin insertar mensaje y deja el Recordatorio Omitido, sin reintento. No depende de columnas de las migraciones 16 o 17 para contar.

El consentimiento de los avisos por WhatsApp continúa requiriendo la tabla existente cuando se enciende su llave. Esta tarea no la crea ni modifica los topes del proceso Node.

## Bandeja

- Compositor bloqueado, candado de `shared/ui.js` y explicación acordada al cerrar la ventana.
- Tiempo restante calculado con reloj monotónico; un temporizador bloquea al vencer sin necesitar otro polling. Cada respuesta del servidor reemplaza la estimación.
- El 409 específico elimina la burbuja pendiente, devuelve el texto y muestra la explicación. Conserva también el borrador nuevo y guarda el texto rechazado si se cambió a otra conversación.
- Un borrador combinado de más de 4000 caracteres se conserva y no se envía hasta acortarlo, para evitar recortarlo silenciosamente.
- Los demás errores conservan la burbuja y el reintento anteriores.

## Pruebas y aislamiento

Hay pruebas con reloj fijo del límite exacto, 23 h 59 min, fecha futura y ausencia de entrantes; consulta real con más de 20 bajas consecutivas; rechazo en servicio y ruta sin fila; lista/detalle; consentimiento estricto; cupo configurable, Pendiente, concurrencia, varios canales y medianoche local; omisión sin reintento; y funciones reales de bandeja.js ejecutadas con Node y DOM mínimo siguiendo `WhatsAppEstadoJsTests`.

Se corrigió contaminación de `DatosDePrueba`: recorría por separado las columnas de una FK compuesta y, al limpiar `UsuarioRol`, borraba permisos de otros usuarios que compartían `IdRol`. Ahora agrupa cada FK y exige coincidencia de todas sus columnas. La regresión reproducía 0 permisos en vez de 2 con el helper anterior y pasa con el corregido. No se modificó la autenticación de la aplicación.

El test de ingreso que detectó el problema pasó aislado con el paso 3. Se corrieron dos suites sobre el commit 2 limpio: una reprodujo una pérdida de Administración y otra pasó 2275/2275. La primera tuvo 11 omisiones automáticas de Node por dependencias ausentes en el worktree; la segunda ya tenía las dependencias y no tuvo omisiones. Con la limpieza corregida, el paso 3 pasó 2279/2279.

## Validación local

Siempre contra bases descartables mediante los scripts del repositorio, sin apuntar a `yosoy`:

```powershell
.\Scripts\correr-tests.ps1 -Base yosoy_test

# Base NUEVA, sin las piezas de consentimiento y chat Personal solicitadas.
.\Scripts\crear-base-de-pruebas.ps1 -Base yosoy_test_ventana_prod_20261021 -SinConsentimientoWhatsApp -SinMigracion17
.\Scripts\correr-tests.ps1 -Base yosoy_test_ventana_prod_20261021 -Filtro 'Migracion!=25&Migracion!=17&Tabla!=ConsentimientoWhatsApp'
```

El filtro de la segunda base incluye `Migracion!=17`, confirmado por el usuario, porque los tests específicos de esas columnas no pueden correr sin ellas. Los resultados definitivos y hashes por commit se registran en el informe de revisión generado fuera del repositorio.

## Límites de la validación

La lista consulta la ventana por conversación y no se midió su rendimiento con miles de conversaciones. Las pruebas de interfaz comprueban lógica y cambios de DOM con Node; no equivalen a una inspección visual en un navegador real. Los controles de WhatsApp del proceso Node se mantienen como una capa adicional.

## Pendiente

- La lista consulta `UltimoEntrante` una vez por conversación. Si el volumen crece, pasarlo a una sola consulta.
- Para encender los avisos de WhatsApp en producción hace falta aplicar la migración 16 y tener consentimiento registrado: ahora `SinDecision` queda `Omitido`.
