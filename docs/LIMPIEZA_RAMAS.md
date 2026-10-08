# Limpieza de ramas — solo después de "MERGE OK"

**No ejecutar nada de esto hasta que se confirme que el PR de integración ya se mergeó a
la rama principal.** Antes de borrar cada rama remota, el Paso 7 vuelve a confirmar con
`git merge-base --is-ancestor` que esa rama está de verdad contenida en la principal —
esta lista es el plan, no una autorización a borrar a ciegas.

**`videollamadas-seguras` queda fuera de esta lista a propósito — no se toca ni se
borra.** Es de un compañero, solo lectura.

## Ramas remotas (`origin`) a borrar

```
git push origin --delete fix-registro-publico
git push origin --delete fix-xss
git push origin --delete Cambios-Agus-jwt
git push origin --delete mejora-chats-y-centrado
git push origin --delete cierre-seguridad
git push origin --delete integracion-main
```

Nota: al momento de escribir esto (ver `CAMBIOS_AGUS.md`, Paso 0), `mejora-chats-y-
centrado` y `cierre-seguridad` **nunca se publicaron** — son locales únicamente. Si para
cuando llegue este paso siguen sin tener rama remota, esos dos comandos no van a
encontrar nada que borrar en `origin`; no es un error, solo confirma que nunca hizo falta
pushear antes de este PR.

## Ramas locales a borrar

Las mismas seis de arriba, más dos ramas de respaldo y una ref de un `filter-branch`
anterior que el pedido original menciona como parte de la limpieza:

```
git branch -d fix-registro-publico
git branch -d fix-xss
git branch -d Cambios-Agus-jwt
git branch -d mejora-chats-y-centrado
git branch -d cierre-seguridad
git branch -d integracion-main
git branch -D backup-pre-limpieza
git branch -D Cambios-Agus
git update-ref -d refs/original/refs/heads/Cambios-Agus-jwt
```

`-D` (mayúscula, fuerza sin chequear si está mergeada) solo para las dos de respaldo —
son copias de seguridad, no ramas de trabajo con historia que deba estar contenida en
ningún lado. Las seis de la cadena usan `-d` (minúscula): si alguna no estuviera
realmente contenida en la principal, este comando se niega solo, en vez de perder commits
sin avisar.

**Nota (ver `CAMBIOS_AGUS.md`, Paso 0):** al momento de escribir esto, `backup-pre-
limpieza`, `Cambios-Agus` y la ref `refs/original/refs/heads/Cambios-Agus-jwt` **no
existen** en este repositorio. No es un error ni algo para investigar — el Paso 7
simplemente no va a encontrar nada que borrar ahí, y esos tres comandos van a fallar con
"no encontrado" (esperable, no hay que insistir ni forzar nada).

## Después de borrar

```
git reflog expire --expire=now --all
git gc --prune=now
```

Y, si el merge en GitHub generó un commit nuevo en la rama principal que corresponda
etiquetar como la entrega, mover `entrega-tesis-v1` ahí (por nombre, confirmando antes
cuál es ese commit — ver el Paso 6/7 del pedido original).
