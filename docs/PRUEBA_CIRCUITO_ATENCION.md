# Prueba del circuito de atención (octubre 2026)

Guía para probar, de punta a punta, lo que agrega esta versión: consentimientos opcionales,
interruptores de IA y transcripción, indicador para el paciente y evolución por enfoque.

## 1. Preparar

1. Correr `migrations/add_atencion_consentimientos_ia_evolucion.sql` (migración 15) sobre la base `yosoy`:
   ```bash
   sqlcmd -S TU_SERVIDOR -d yosoy -E -C -f 65001 -i migrations\add_atencion_consentimientos_ia_evolucion.sql
   ```
2. `dotnet test YoSoy.Tests`: tienen que pasar las pruebas de siempre más `FuncionesSesionServiceTests` y `EvolucionEnfoquesTests`.
3. Levantar la API (`dotnet run`) y el servidor de señalización (`cd signaling-server && npm start`).
4. Tener un paciente con turno confirmado con un profesional (por ejemplo, los de `docs/datos-demo.sql`).

## 2. Paciente: consentimientos opcionales

1. Ingresar como paciente → menú **Consentimiento**.
2. Debajo del de telemedicina aparecen tres: análisis de expresiones por IA, transcripción y WhatsApp.
3. Aceptar el de IA. **Esperado:** pasa a "Aceptado" con fecha y hora.
4. No aceptar el de transcripción todavía.

## 3. Profesional: preferencias

1. Ingresar como profesional → **Mi perfil** → **Preferencias de sesión**.
2. Elegir enfoque **TCC**, registro **Manual**, y tildar "Encender el análisis de expresiones al iniciar".
3. Guardar.

## 4. Sesión

1. Profesional: **Inicio** → **Ir a sala** en el turno del paciente. La sala usa el mismo código que le llegó al paciente.
2. **Esperado** en el resumen: "Aceptó el análisis de expresiones" y "Sin consentimiento para transcripción".
3. Entrar a la sala. El paciente entra desde su aviso o desde **Sala de video**.
4. **Esperado:**
   - el análisis de expresiones arranca encendido (por la preferencia);
   - el interruptor de transcripción está deshabilitado ("El paciente no dio su consentimiento");
   - el paciente ve el aviso **"Análisis de expresiones activo"**.
5. Paciente: tocar **Pedir que se apague**. **Esperado:** el aviso desaparece y al profesional le llega el aviso; su interruptor queda apagado.
6. Profesional: volver a encenderlo. **Esperado:** el paciente vuelve a ver el aviso.
7. Paciente, en otra pestaña: **Consentimiento** → revocar el de IA. **Esperado:** en 10 segundos como máximo, el interruptor del profesional queda deshabilitado y el análisis se apaga solo.

## 5. Evolución

1. Con enfoque TCC, completar situación, pensamiento, emoción y conducta.
2. Aplicar el PHQ-9 y marcar el ítem 9 en "Varios días". **Esperado:** el resultado muestra la alerta del ítem 9.
3. **Firmar evolución** sin elegir tipo de cierre. **Esperado:** el servidor rechaza y pide el tipo de cierre (RN-08).
4. Elegir **Seguimiento** → **Guardar borrador** → **Firmar evolución**. **Esperado:** queda firmada y el formulario se bloquea.
5. Cambiar a **Psicoanálisis**, **Sistémica** e **Infanto-juvenil** en otra sesión y verificar sus campos:
   - Psicoanálisis: nota libre.
   - Sistémica: consultantes con vínculo y si estuvieron presentes.
   - Infanto-juvenil: qué se le devuelve al tutor; con "¿Se detectaron indicadores de riesgo?" en **Sí** aparece la alerta.
6. Modo **Dictado**: aparece "Dictar en el campo seleccionado". Modos con transcripción: solo disponibles si el paciente aceptó la transcripción.

## 6. Historia clínica

1. **Mis pacientes** → **Historia clínica** → **Evoluciones**. **Esperado:** la evolución con su enfoque, chip "Firmada" y las escalas con su resultado.
2. **Agregar addendum**. **Esperado:** aparece debajo con autor y fecha; la evolución original no cambia.
3. Prueba de RN-11 en la base:
   ```sql
   UPDATE Evolucion SET Observaciones = 'cambio' WHERE Estado = 'Firmada';
   ```
   **Esperado:** error 50011 "RN-11: una evolución firmada no se puede modificar".

## 7. Entrevista de admisión (Configurador)

1. **Admisiones** → programar entrevista → **Iniciar entrevista**.
2. **Esperado:**
   - la sala reconoce al paciente y respeta sus consentimientos;
   - el panel de evolución indica que la evolución inicial se escribe al derivar.
