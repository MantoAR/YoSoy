#!/usr/bin/env bash
# =====================================================================================================
# generar-secretos.sh — secretos de las videollamadas de YoSoy.
#
# Genera con `openssl rand -hex 32` el secreto de la señalización y el de TURN y los escribe donde hacen
# falta, SIN imprimirlos nunca: ni en pantalla, ni en los errores, ni en la línea de comandos de ningún
# otro proceso (solo se mueven por builtins de bash y por pipes).
#
#   /opt/yosoy/config/signaling.env   SIGNALING_SECRETO, HOST, PORT     lo lee signaling-server.service
#   /opt/yosoy/config/video.env       Video__SignalingSecreto (= SIGNALING_SECRETO), Video__TurnSecreto,
#                                     URLs STUN/TURN y zona horaria     lo lee yosoy-api (drop-in video.conf)
#   /etc/turnserver.conf              static-auth-secret (= Video__TurnSecreto)   lo lee coturn
#
# Uso, en el VPS:   sudo bash deploy/video/generar-secretos.sh [opciones]
#
#   --rotar-signaling   Genera un secreto NUEVO de la señalización y lo escribe en signaling.env y video.env.
#   --rotar-turn        Genera un secreto NUEVO de TURN y lo escribe en video.env y en /etc/turnserver.conf.
#   --dominio DOMINIO   Dominio de las URLs STUN/TURN de video.env (por defecto yosoytelemedicina.tech).
#   -h, --ayuda         Muestra esta ayuda.
#
# Es idempotente: sin opciones NO pisa ningún secreto que ya exista. Lo que falta lo crea, lo que ya está
# igual en todos lados lo deja como está, y puede correrse las veces que haga falta. Si los archivos
# discrepan (dos valores distintos para el mismo secreto) se detiene SIN tocar nada y lo dice. Antes de
# reemplazar un secreto existente (solo con --rotar-*) deja una copia ARCHIVO.previo-AAAAMMDD-HHMMSS con los
# mismos permisos: borrala cuando confirmes que todo anda.
#
# Hay que correrlo DOS veces en el despliegue: en el paso c (crea los .env) y otra vez en el paso d, después
# de instalar coturn y copiar turnserver.conf, para que escriba ahí el mismo secreto de TURN. Si coturn
# todavía no está, avisa y sigue (no es un error).
#
# Jwt__Key: este script NO lee el .env de la API (ahí están sus secretos), así que no puede comparar. No hace
# falta: los secretos salen de openssl (256 bits al azar), por lo que no pueden coincidir con Jwt__Key salvo
# que alguien los edite a mano. Y la propia API lo verifica al arrancar: si Video__SignalingSecreto fuera igual
# a Jwt__Key, escribe "Video: /video/acceso deshabilitado ..." en el journal (docs/DESPLIEGUE_VIDEO.md, paso f).
#
# Variables YS_* (opcionales): existen para poder probar el script sin ser root ni tocar /opt ni /etc.
#   YS_CONFIG_DIR, YS_TURN_CONF, YS_DUENO, YS_GRUPO, YS_TURN_DUENO, YS_TURN_GRUPO, YS_DOMINIO,
#   YS_ZONA_HORARIA e YS_SIN_ROOT=1 (salta el chequeo de root y el de dueños y permisos).
# =====================================================================================================
set +x                       # nunca trazar: la traza mostraría los valores
set -euo pipefail
umask 077                    # todo lo que se cree nace sin permisos para otros
export LC_ALL=C              # los rangos [A-Za-z0-9] de abajo valen solo para ASCII, en cualquier locale

CONFIG_DIR="${YS_CONFIG_DIR:-/opt/yosoy/config}"
TURN_CONF="${YS_TURN_CONF:-/etc/turnserver.conf}"
DUENO="${YS_DUENO:-yosoy}"
GRUPO="${YS_GRUPO:-yosoy}"
TURN_DUENO="${YS_TURN_DUENO:-root}"
TURN_GRUPO="${YS_TURN_GRUPO:-turnserver}"
DOMINIO="${YS_DOMINIO:-yosoytelemedicina.tech}"
ZONA="${YS_ZONA_HORARIA:-America/Argentina/Buenos_Aires}"
SIN_ROOT="${YS_SIN_ROOT:-}"

SIG_ENV="$CONFIG_DIR/signaling.env"
VIDEO_ENV="$CONFIG_DIR/video.env"
MARCA_TURN='# >>> static-auth-secret:'     # la deja deploy/coturn/turnserver.conf; la línea del secreto va debajo
SECRETO_MIN=32                            # el mínimo que exigen la API y el signaling
SELLO="$(date +%Y%m%d-%H%M%S)"

ROTAR_SIGNALING=0
ROTAR_TURN=0
FALLOS=0
TEMPORALES=()
RESPALDOS=()
CAMBIOS=()

# ----------------------------------------------------------------------------------------- mensajes
fallar() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
ok()     { printf '  [ok] %s\n' "$*"; }
mal()    { printf '  [FALLÓ] %s\n' "$*"; FALLOS=$((FALLOS + 1)); }
aviso()  { printf '  [!] %s\n' "$*"; }
info()   { printf '  [i] %s\n' "$*"; }

uso() {
    sed -n '3,/^# =====/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'
}

limpiar() {
    local t
    for t in "${TEMPORALES[@]:-}"; do
        if [ -n "$t" ]; then rm -f "$t"; fi
    done
}
trap limpiar EXIT

# ----------------------------------------------------------------------------------------- argumentos
while [ $# -gt 0 ]; do
    case "$1" in
        --rotar-signaling) ROTAR_SIGNALING=1; shift ;;
        --rotar-turn)      ROTAR_TURN=1; shift ;;
        --dominio)
            if [ $# -lt 2 ]; then fallar "--dominio necesita un valor."; fi
            DOMINIO=$2; shift 2 ;;
        --dominio=*)       DOMINIO=${1#*=}; shift ;;
        -h|--ayuda|--help) uso; exit 0 ;;
        *)                 fallar "Opción desconocida: $1 (probá con --ayuda)." ;;
    esac
done

# ----------------------------------------------------------------------------------------- chequeos previos
if ! command -v openssl >/dev/null 2>&1; then fallar "Falta openssl."; fi
if [ -z "$SIN_ROOT" ]; then
    if [ "$(id -u)" -ne 0 ]; then fallar "Correlo como root: sudo bash $0"; fi
    if ! id -u "$DUENO" >/dev/null 2>&1; then fallar "No existe el usuario $DUENO."; fi
fi
if [ ! -d "$CONFIG_DIR" ]; then fallar "No existe $CONFIG_DIR."; fi
if ! [[ $DOMINIO =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]]; then
    fallar "El dominio \"$DOMINIO\" no es válido (letras, números, puntos y guiones)."
fi

# ----------------------------------------------------------------------------------------- utilidades
nuevo_secreto() { openssl rand -hex 32 | tr -d '\r\n'; }

# Un secreto sirve si tiene al menos 32 caracteres y solo de los que systemd y .NET leen tal cual (sin
# espacios, comillas, #, \ ni $): así el valor del .env, el de la API y el del signaling nunca difieren.
secreto_valido() {
    local v=$1
    [ "${#v}" -ge "$SECRETO_MIN" ] && [[ $v =~ ^[A-Za-z0-9._~+/=-]+$ ]]
}

# valor_de ARCHIVO CLAVE: imprime el valor de la última línea activa "CLAVE=valor" (nada si no hay).
valor_de() {
    local archivo=$1 clave=$2 linea valor=""
    if [ -f "$archivo" ]; then
        while IFS= read -r linea || [ -n "$linea" ]; do
            linea=${linea%$'\r'}
            if [[ $linea == "$clave="* ]]; then valor=${linea#*=}; fi
        done < "$archivo"
    fi
    printf '%s' "$valor"
}

# tiene_prefijo ARCHIVO PREFIJO: ¿hay alguna línea activa que empiece con PREFIJO? (para las listas Algo__0, Algo__1...)
tiene_prefijo() {
    local linea
    if [ ! -f "$1" ]; then return 1; fi
    while IFS= read -r linea || [ -n "$linea" ]; do
        if [[ $linea == "$2"* ]]; then return 0; fi
    done < "$1"
    return 1
}

# dar_permisos ARCHIVO MODO DUENO GRUPO
dar_permisos() {
    chmod "$2" "$1"
    if [ -z "$SIN_ROOT" ]; then chown "$3:$4" "$1"; fi
}

# temporal_de ARCHIVO MODO DUENO GRUPO: crea un temporal en el MISMO directorio con los permisos finales
# (antes de que se escriba nada ahí dentro) y deja su ruta en $TMP.
temporal_de() {
    TMP="$(mktemp "$(dirname "$1")/.$(basename "$1").XXXXXX")"
    TEMPORALES+=("$TMP")
    dar_permisos "$TMP" "$2" "$3" "$4"
}

# crear_archivo ARCHIVO MODO DUENO GRUPO: crea ARCHIVO con lo que llega por stdin.
crear_archivo() {
    temporal_de "$1" "$2" "$3" "$4"
    cat > "$TMP"
    mv -f "$TMP" "$1"
}

# poner_clave ARCHIVO CLAVE VALOR MODO DUENO GRUPO: reemplaza la línea CLAVE=... (o la agrega al final) y
# deja todo lo demás tal cual. Escribe en un temporal y lo mueve encima: nunca queda un archivo a medias.
poner_clave() {
    local archivo=$1 clave=$2 valor=$3 linea hecho=0
    temporal_de "$archivo" "$4" "$5" "$6"
    {
        while IFS= read -r linea || [ -n "$linea" ]; do
            if [[ ${linea%$'\r'} == "$clave="* ]]; then
                if [ "$hecho" -eq 0 ]; then printf '%s=%s\n' "$clave" "$valor"; hecho=1; fi
                continue
            fi
            printf '%s\n' "$linea"
        done < "$archivo"
        if [ "$hecho" -eq 0 ]; then printf '%s=%s\n' "$clave" "$valor"; fi
    } > "$TMP"
    mv -f "$TMP" "$archivo"
}

# poner_secreto_turn ARCHIVO VALOR: deja UNA sola línea static-auth-secret=VALOR en la config de coturn: la
# reemplaza si ya hay una; si no, la escribe debajo de la marca de turnserver.conf; y si tampoco hay marca,
# al final. No toca ninguna otra línea.
poner_secreto_turn() {
    local archivo=$1 valor=$2 linea existe=0 hecho=0
    if tiene_prefijo "$archivo" "static-auth-secret="; then existe=1; fi
    temporal_de "$archivo" 640 "$TURN_DUENO" "$TURN_GRUPO"
    {
        while IFS= read -r linea || [ -n "$linea" ]; do
            if [[ ${linea%$'\r'} == "static-auth-secret="* ]]; then
                if [ "$hecho" -eq 0 ]; then printf 'static-auth-secret=%s\n' "$valor"; hecho=1; fi
                continue
            fi
            printf '%s\n' "$linea"
            if [ "$existe" -eq 0 ] && [ "$hecho" -eq 0 ] && [[ $linea == "$MARCA_TURN"* ]]; then
                printf 'static-auth-secret=%s\n' "$valor"; hecho=1
            fi
        done < "$archivo"
        if [ "$hecho" -eq 0 ]; then printf 'static-auth-secret=%s\n' "$valor"; fi
    } > "$TMP"
    mv -f "$TMP" "$archivo"
}

# respaldar ARCHIVO: una copia ARCHIVO.previo-SELLO con los mismos permisos, una sola vez por corrida.
respaldar() {
    local copia="$1.previo-$SELLO"
    if [ -f "$1" ] && [ ! -e "$copia" ]; then
        cp -p "$1" "$copia"
        RESPALDOS+=("$copia")
    fi
}

# elegir NOMBRE ROTAR OPCION valor...: decide el secreto a usar a partir de lo que ya hay (los valores vacíos
# no cuentan). Deja el resultado en ELEGIDO y ESTADO (generado | existente | rotado). Nunca imprime un valor.
elegir() {
    local nombre=$1 rotar=$2 opcion=$3 v d ya hubo=0
    local distintos=()
    shift 3
    for v in "$@"; do
        if [ -z "$v" ]; then continue; fi
        hubo=1
        if [ "$rotar" -eq 0 ] && ! secreto_valido "$v"; then
            fallar "$nombre: el valor que ya hay no sirve (menos de $SECRETO_MIN caracteres o con caracteres que systemd no lee tal cual). No se muestra. Para reemplazarlo: $opcion"
        fi
        ya=0
        for d in "${distintos[@]:-}"; do
            if [ "$d" = "$v" ]; then ya=1; fi
        done
        if [ "$ya" -eq 0 ]; then distintos+=("$v"); fi
    done
    if [ "$rotar" -eq 1 ]; then
        ELEGIDO="$(nuevo_secreto)"
        if [ "$hubo" -eq 1 ]; then ESTADO=rotado; else ESTADO=generado; fi
    elif [ "${#distintos[@]}" -eq 0 ]; then
        ELEGIDO="$(nuevo_secreto)"; ESTADO=generado
    elif [ "${#distintos[@]}" -eq 1 ]; then
        ELEGIDO=${distintos[0]}; ESTADO=existente
    else
        fallar "$nombre: los archivos tienen valores DISTINTOS para el mismo secreto (no se muestran) y no sé cuál es el bueno. No se tocó nada. Para dejar uno nuevo e igual en todos: $opcion"
    fi
    if ! secreto_valido "$ELEGIDO"; then fallar "$nombre: no se pudo obtener un secreto válido (¿openssl?)."; fi
}

# aplicar_secreto ARCHIVO CLAVE VALOR MODO DUENO GRUPO: si el archivo no tiene ya ese valor, lo escribe
# (con copia previa si había otro).
aplicar_secreto() {
    local actual accion=escrito
    actual="$(valor_de "$1" "$2")"
    if [ "$actual" = "$3" ]; then return 0; fi
    if [ -n "$actual" ]; then respaldar "$1"; accion=reemplazado; fi
    poner_clave "$1" "$2" "$3" "$4" "$5" "$6"
    CAMBIOS+=("$(basename "$1"): $2 $accion")
}

# agregar_si_falta ARCHIVO CLAVE VALOR: agrega una clave sin valor secreto solo si no existe; nunca pisa una que ya está.
agregar_si_falta() {
    if [ -z "$(valor_de "$1" "$2")" ]; then
        poner_clave "$1" "$2" "$3" 600 "$DUENO" "$GRUPO"
        CAMBIOS+=("$(basename "$1"): $2 agregado")
    fi
}

# =============================================================================== 1. decidir (sin escribir)
SIG_A="$(valor_de "$SIG_ENV" SIGNALING_SECRETO)"
SIG_B="$(valor_de "$VIDEO_ENV" Video__SignalingSecreto)"
TURN_A="$(valor_de "$VIDEO_ENV" Video__TurnSecreto)"
TURN_B=""
if [ -f "$TURN_CONF" ]; then TURN_B="$(valor_de "$TURN_CONF" static-auth-secret)"; fi

elegir "Secreto de la señalización" "$ROTAR_SIGNALING" "--rotar-signaling" "$SIG_A" "$SIG_B"
SIG="$ELEGIDO"; SIG_ESTADO="$ESTADO"
elegir "Secreto de TURN" "$ROTAR_TURN" "--rotar-turn" "$TURN_A" "$TURN_B"
TURN="$ELEGIDO"; TURN_ESTADO="$ESTADO"

if [ "$SIG" = "$TURN" ]; then
    fallar "El secreto de la señalización y el de TURN son el mismo (no se muestra). Tienen que ser distintos: rotá uno con --rotar-signaling o --rotar-turn."
fi

TURN_CONF_LISTA=0
if [ -f "$TURN_CONF" ]; then
    TURN_CONF_LISTA=1
    if [ -z "$SIN_ROOT" ] && ! getent group "$TURN_GRUPO" >/dev/null 2>&1; then
        TURN_CONF_LISTA=0
        aviso "Existe $TURN_CONF pero no el grupo $TURN_GRUPO (¿coturn sin instalar?): no se escribe ahí."
    fi
fi

# =============================================================================== 2. escribir
if [ ! -f "$SIG_ENV" ]; then
    {
        printf '%s\n' "# signaling-server: variables de entorno (las carga signaling-server.service)."
        printf '%s\n' "# Dueño yosoy, permisos 600. Creado por deploy/video/generar-secretos.sh. NO va al repositorio."
        printf '%s\n' "# Documentación de cada variable: signaling-server/README.md. Sin comillas ni espacios alrededor del =."
        printf 'SIGNALING_SECRETO=%s\n' "$SIG"
        printf '%s\n' "HOST=127.0.0.1"
        printf '%s\n' "PORT=3001"
    } | crear_archivo "$SIG_ENV" 600 "$DUENO" "$GRUPO"
    CAMBIOS+=("signaling.env: creado")
else
    aplicar_secreto "$SIG_ENV" SIGNALING_SECRETO "$SIG" 600 "$DUENO" "$GRUPO"
    agregar_si_falta "$SIG_ENV" HOST 127.0.0.1
    agregar_si_falta "$SIG_ENV" PORT 3001
fi

if [ ! -f "$VIDEO_ENV" ]; then
    {
        printf '%s\n' "# yosoy-api: variables de las videollamadas (las carga el drop-in yosoy-api.service.d/video.conf)."
        printf '%s\n' "# Dueño yosoy, permisos 600. Creado por deploy/video/generar-secretos.sh. NO va al repositorio."
        printf '%s\n' "# Documentación de cada variable: docs/VIDEOLLAMADAS.md (\"Configuración\"). Sin comillas ni espacios alrededor del =."
        printf '%s\n' "# Video__SignalingSecreto tiene que ser IGUAL a SIGNALING_SECRETO de signaling.env."
        printf 'Video__SignalingSecreto=%s\n' "$SIG"
        printf 'Video__TurnSecreto=%s\n' "$TURN"
        printf 'Video__Ice__StunUrls__0=stun:%s:3478\n' "$DOMINIO"
        printf 'Video__Ice__TurnUrls__0=turn:%s:3478?transport=udp\n' "$DOMINIO"
        printf 'Video__Ice__TurnUrls__1=turn:%s:3478?transport=tcp\n' "$DOMINIO"
        printf 'Video__ZonaHoraria=%s\n' "$ZONA"
    } | crear_archivo "$VIDEO_ENV" 600 "$DUENO" "$GRUPO"
    CAMBIOS+=("video.env: creado")
else
    aplicar_secreto "$VIDEO_ENV" Video__SignalingSecreto "$SIG" 600 "$DUENO" "$GRUPO"
    aplicar_secreto "$VIDEO_ENV" Video__TurnSecreto "$TURN" 600 "$DUENO" "$GRUPO"
    # Las listas de URLs se completan solo si no hay NINGUNA entrada (si ya las personalizaste, quedan como están).
    if ! tiene_prefijo "$VIDEO_ENV" "Video__Ice__StunUrls__"; then
        poner_clave "$VIDEO_ENV" Video__Ice__StunUrls__0 "stun:$DOMINIO:3478" 600 "$DUENO" "$GRUPO"
        CAMBIOS+=("video.env: URL de STUN agregada")
    fi
    if ! tiene_prefijo "$VIDEO_ENV" "Video__Ice__TurnUrls__"; then
        poner_clave "$VIDEO_ENV" Video__Ice__TurnUrls__0 "turn:$DOMINIO:3478?transport=udp" 600 "$DUENO" "$GRUPO"
        poner_clave "$VIDEO_ENV" Video__Ice__TurnUrls__1 "turn:$DOMINIO:3478?transport=tcp" 600 "$DUENO" "$GRUPO"
        CAMBIOS+=("video.env: URLs de TURN agregadas")
    fi
    agregar_si_falta "$VIDEO_ENV" Video__ZonaHoraria "$ZONA"
fi

if [ "$TURN_CONF_LISTA" -eq 1 ]; then
    ACTUAL_TURN="$(valor_de "$TURN_CONF" static-auth-secret)"
    if [ "$ACTUAL_TURN" != "$TURN" ]; then
        if [ -n "$ACTUAL_TURN" ]; then respaldar "$TURN_CONF"; fi
        poner_secreto_turn "$TURN_CONF" "$TURN"
        CAMBIOS+=("$(basename "$TURN_CONF"): static-auth-secret escrito")
    fi
fi

# Dueños y permisos: se aseguran siempre, aunque el contenido no haya cambiado.
dar_permisos "$SIG_ENV" 600 "$DUENO" "$GRUPO"
dar_permisos "$VIDEO_ENV" 600 "$DUENO" "$GRUPO"
if [ "$TURN_CONF_LISTA" -eq 1 ]; then dar_permisos "$TURN_CONF" 640 "$TURN_DUENO" "$TURN_GRUPO"; fi

# =============================================================================== 3. informe y verificación
echo "YoSoy · secretos de las videollamadas (ningún valor se muestra)"
echo
echo "Qué se hizo:"
printf '  Secreto de la señalización ... %s\n' "$SIG_ESTADO"
printf '  Secreto de TURN .............. %s\n' "$TURN_ESTADO"
if [ "${#CAMBIOS[@]}" -eq 0 ]; then
    echo "  Archivos ..................... sin cambios (ya estaba todo)"
else
    for c in "${CAMBIOS[@]}"; do printf '  Archivos ..................... %s\n' "$c"; done
fi
if [ "$TURN_CONF_LISTA" -eq 0 ] && [ ! -f "$TURN_CONF" ]; then
    info "$TURN_CONF todavía no existe: instalá coturn y copiá turnserver.conf (paso d) y volvé a correr este script."
fi
for r in "${RESPALDOS[@]:-}"; do
    if [ -n "$r" ]; then aviso "Copia del archivo anterior: $r (borrala cuando confirmes que todo anda)."; fi
done

echo
echo "Verificación:"

# Releo todo desde disco y comparo en memoria.
V_SIG_A="$(valor_de "$SIG_ENV" SIGNALING_SECRETO)"
V_SIG_B="$(valor_de "$VIDEO_ENV" Video__SignalingSecreto)"
V_TURN_A="$(valor_de "$VIDEO_ENV" Video__TurnSecreto)"

if secreto_valido "$V_SIG_A" && [ "$V_SIG_A" = "$V_SIG_B" ]; then
    ok "SIGNALING_SECRETO (signaling.env) es igual a Video__SignalingSecreto (video.env), de ${#V_SIG_A} caracteres."
else
    mal "SIGNALING_SECRETO y Video__SignalingSecreto no coinciden o no son válidos."
fi
if secreto_valido "$V_TURN_A" && [ "$V_TURN_A" != "$V_SIG_A" ]; then
    ok "Video__TurnSecreto es válido y distinto del de la señalización."
else
    mal "Video__TurnSecreto no es válido o es igual al de la señalización."
fi
if [ "$TURN_CONF_LISTA" -eq 1 ]; then
    if [ "$(valor_de "$TURN_CONF" static-auth-secret)" = "$V_TURN_A" ]; then
        ok "static-auth-secret ($TURN_CONF) es igual a Video__TurnSecreto."
    else
        mal "static-auth-secret no coincide con Video__TurnSecreto."
    fi
fi

V_HOST="$(valor_de "$SIG_ENV" HOST)"
V_PORT="$(valor_de "$SIG_ENV" PORT)"
case "$V_HOST" in
    127.0.0.1|localhost|::1) ok "HOST=$V_HOST (solo loopback: el signaling no se expone sin Caddy)." ;;
    *) mal "HOST=$V_HOST no es loopback: el signaling quedaría expuesto sin pasar por Caddy." ;;
esac
if [ "$V_PORT" != "3001" ]; then aviso "PORT=$V_PORT: deploy/caddy/video.caddy apunta al 3001; cambialo también ahí."; fi

if [ -z "$SIN_ROOT" ]; then
    verificar_permisos() {   # verificar_permisos ARCHIVO "600 dueño:grupo"
        local real
        real="$(stat -c '%a %U:%G' "$1")"
        if [ "$real" = "$2" ]; then ok "$(basename "$1"): $real"; else mal "$(basename "$1"): $real (tiene que ser $2)"; fi
    }
    verificar_permisos "$SIG_ENV" "600 $DUENO:$GRUPO"
    verificar_permisos "$VIDEO_ENV" "600 $DUENO:$GRUPO"
    if [ "$TURN_CONF_LISTA" -eq 1 ]; then verificar_permisos "$TURN_CONF" "640 $TURN_DUENO:$TURN_GRUPO"; fi
fi

info "Jwt__Key: no se compara (este script no lee el .env de la API). Los secretos salen de openssl, así que no pueden"
info "coincidir con él salvo edición manual; la API lo verifica al arrancar (journal: \"Video: /video/acceso habilitado.\")."

echo
if [ "$FALLOS" -gt 0 ]; then
    echo "Hubo $FALLOS verificación(es) que fallaron: revisá lo de arriba antes de seguir."
    exit 1
fi
if [ "${#RESPALDOS[@]}" -gt 0 ] || [ "$SIG_ESTADO" = "rotado" ] || [ "$TURN_ESTADO" = "rotado" ]; then
    echo "Hubo rotación: reiniciá lo que usa el secreto nuevo (signaling-server, coturn y yosoy-api) y probá una llamada."
fi
echo "Listo."
