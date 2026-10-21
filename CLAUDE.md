# Reglas de trabajo en YoSoy (para Claude)

- Responder en español. Commits en español, como los últimos del repo. Trailer de Claude según corresponda.
- Nunca commit, push ni merge sin OK del dueño. Nunca `--force`. No tocar ramas de otras personas con rebase, reset ni force; conservar su autoría (lo que se extrae de su trabajo lleva `Co-Authored-By`).
- Nunca leer ni imprimir `.env`, `whatsapp.env`, `signaling.env`, `video.env`. Sin secretos, contraseñas ni teléfonos en código, logs, docs, tests ni commits.
- Pruebas de integración solo contra `yosoy_test` (o una base descartable que se crea y se borra), jamás contra `yosoy` (producción). Al informar cantidades de pruebas, dar el comando.
- Migraciones aditivas e idempotentes; se aplican con `sqlcmd -I -b -f 65001`. Cada cambio de base lleva ensayo sobre una base descartable con la forma de producción y plan de vuelta atrás.
- Despliegue siempre manual, con runbook de pasos de una línea (dónde corre cada uno: PC o VPS, qué esperar, cómo volver atrás). Backups de base solo en la PC del dueño, fuera de cualquier repo. Claude no tiene sudo ni contraseñas de base: las tipea el dueño.
- PR: título y cuerpo en `D:\Claude\YoSoy\enlaces-pr\<nombre>.txt` y link para abrirlo contra `main`; el dueño mergea con "Create a merge commit". No hay CLI `gh`.
- Informes cortos: resumen, `git diff --stat` y diff completo solo de lo pedido.

## Reglas de producto vigentes (no romperlas)

- Los Chats (`/mensajeria`) los ve y responde solo el módulo CRM; quien tiene además el permiso Configurador entra a la sesión única "Administración" (Configurador + CRM). El CRM puro (la recepcionista) no cambia: solo chats. El Configurador solo no ve chats. Los profesionales reciben siempre 403.
- El simulador `/mensajeria/simulado/*` responde solo a la propia máquina.
- `Mensajeria:ModoAtencion=Personal`; solo texto; no hay recordatorios ni asistente por WhatsApp.
- El log nunca lleva texto de mensajes ni teléfonos completos.
- El cifrado de emociones y el consentimiento por sala de video de `main` no se tocan.
