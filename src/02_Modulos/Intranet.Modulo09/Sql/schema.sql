-- ============================================================================
-- 🗄️ ESQUEMA SOBERANO mod09: TESORERÍA, TRÁMITES TUPA Y PAGOS (Equipo 09)
-- Portado desde db_intranet_iestp local (schema 3.2.0) a las reglas del
-- proyecto: FKs INT al core de Felipe, DDL idempotente, snake_case.
-- Historia: diseño del equipo 09 (54 conceptos TUPA reales del IESTP),
-- normalizado 3NF, verificado con pruebas negativas en el repo
-- gestion-estudiantes-intranet.
-- ============================================================================

-- 1. Catálogo: tipos de pago (Efectivo, Transferencia...)
CREATE TABLE IF NOT EXISTS mod09.tipos_pago (
    id SERIAL PRIMARY KEY,
    codigo VARCHAR(10) NOT NULL UNIQUE,
    nombre VARCHAR(50) NOT NULL,
    activo BOOLEAN NOT NULL DEFAULT TRUE
);

-- 2. Catálogo: conceptos de pago TUPA (los 53 conceptos oficiales)
CREATE TABLE IF NOT EXISTS mod09.conceptos_pago (
    id SERIAL PRIMARY KEY,
    codigo VARCHAR(10) NOT NULL UNIQUE,
    nombre VARCHAR(200) NOT NULL,
    monto NUMERIC(10,2) NOT NULL DEFAULT 0 CHECK (monto >= 0),
    activo BOOLEAN NOT NULL DEFAULT TRUE
);

-- 3. Catálogo: tipos de trámite TUPA (admisión, certificados, titulación...)
CREATE TABLE IF NOT EXISTS mod09.tipos_tramite (
    id SERIAL PRIMARY KEY,
    codigo VARCHAR(10) NOT NULL UNIQUE,
    nombre VARCHAR(200) NOT NULL,
    -- el trámite se paga con un concepto TUPA (FK interna del módulo)
    concepto_pago_id INT NULL REFERENCES mod09.conceptos_pago(id) ON DELETE SET NULL,
    dias_habiles INT NOT NULL DEFAULT 3 CHECK (dias_habiles > 0),
    activo BOOLEAN NOT NULL DEFAULT TRUE
);

-- 4. Catálogo: requisitos por tipo de trámite (checklist TUPA)
CREATE TABLE IF NOT EXISTS mod09.requisitos_tipos_tramite (
    id SERIAL PRIMARY KEY,
    tipo_tramite_id INT NOT NULL REFERENCES mod09.tipos_tramite(id) ON DELETE CASCADE,
    orden INT NOT NULL DEFAULT 1,
    requisito VARCHAR(300) NOT NULL,
    UNIQUE (tipo_tramite_id, orden, requisito)
);

-- 5. Feriados (para cálculo de plazos en días hábiles)
CREATE TABLE IF NOT EXISTS mod09.feriados (
    id SERIAL PRIMARY KEY,
    fecha DATE NOT NULL UNIQUE,
    descripcion VARCHAR(150) NOT NULL
);

-- 6. Trámites TUPA: el expediente del ciudadano
CREATE TABLE IF NOT EXISTS mod09.tramites (
    id SERIAL PRIMARY KEY,
    codigo VARCHAR(20) NOT NULL UNIQUE,
    tipo_tramite_id INT NOT NULL REFERENCES mod09.tipos_tramite(id) ON DELETE RESTRICT,
    estudiante_id INT NULL REFERENCES core.estudiantes(id) ON DELETE SET NULL,
    periodo_id INT NOT NULL REFERENCES core.periodos_academicos(id) ON DELETE RESTRICT,
    fecha_solicitud DATE NOT NULL DEFAULT CURRENT_DATE,
    fecha_limite DATE NULL,
    estado VARCHAR(20) NOT NULL DEFAULT 'Recibido' CHECK (estado IN ('Recibido','En evaluación','Aprobado','Observado','Rechazado','Entregado')),
    datos JSONB NULL,
    resolucion VARCHAR(300) NULL,
    fecha_resolucion DATE NULL,
    creado_en TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    actualizado_en TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_tramites_estudiante ON mod09.tramites (estudiante_id);
CREATE INDEX IF NOT EXISTS idx_tramites_estado ON mod09.tramites (estado);

-- 7. Requisitos presentados por trámite (instancia del checklist)
CREATE TABLE IF NOT EXISTS mod09.tramite_requisitos (
    id SERIAL PRIMARY KEY,
    tramite_id INT NOT NULL REFERENCES mod09.tramites(id) ON DELETE CASCADE,
    requisito_catalogo_id INT NOT NULL REFERENCES mod09.requisitos_tipos_tramite(id) ON DELETE RESTRICT,
    presentado BOOLEAN NOT NULL DEFAULT FALSE,
    observacion VARCHAR(300) NULL,
    UNIQUE (tramite_id, requisito_catalogo_id)
);

-- 8. Pagos TUPA
CREATE TABLE IF NOT EXISTS mod09.pagos (
    id SERIAL PRIMARY KEY,
    codigo VARCHAR(20) NOT NULL UNIQUE,
    estudiante_id INT NOT NULL REFERENCES core.estudiantes(id) ON DELETE RESTRICT,
    concepto_pago_id INT NOT NULL REFERENCES mod09.conceptos_pago(id) ON DELETE RESTRICT,
    periodo_id INT NOT NULL REFERENCES core.periodos_academicos(id) ON DELETE RESTRICT,
    tipo_pago_id INT NOT NULL REFERENCES mod09.tipos_pago(id) ON DELETE RESTRICT,
    monto NUMERIC(10,2) NOT NULL CHECK (monto > 0),
    fecha_pago DATE NOT NULL DEFAULT CURRENT_DATE,
    voucher_estado VARCHAR(20) NOT NULL DEFAULT 'Pendiente' CHECK (voucher_estado IN ('Pendiente','Validado','Rechazado')),
    motivo_rechazo VARCHAR(300) NULL,
    fecha_validacion DATE NULL,
    validado_por INT NULL REFERENCES core.usuarios(id) ON DELETE SET NULL,
    intentos INT NOT NULL DEFAULT 0,
    creado_en TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    actualizado_en TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_pagos_estudiante ON mod09.pagos (estudiante_id);
CREATE INDEX IF NOT EXISTS idx_pagos_periodo ON mod09.pagos (periodo_id);

-- 9. Avisos internos (buzón de confirmaciones para el estudiante)
CREATE TABLE IF NOT EXISTS mod09.avisos (
    id SERIAL PRIMARY KEY,
    remitente_id INT NULL REFERENCES core.usuarios(id) ON DELETE SET NULL,
    tipo VARCHAR(30) NOT NULL DEFAULT 'Información',
    titulo VARCHAR(150) NOT NULL,
    mensaje TEXT NOT NULL,
    creado_en TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- 10. Destinatarios de avisos (muchos por aviso)
CREATE TABLE IF NOT EXISTS mod09.avisos_destinatarios (
    id SERIAL PRIMARY KEY,
    aviso_id INT NOT NULL REFERENCES mod09.avisos(id) ON DELETE CASCADE,
    destinatario_id INT NOT NULL REFERENCES core.usuarios(id) ON DELETE CASCADE,
    leido BOOLEAN NOT NULL DEFAULT FALSE,
    fecha_lectura TIMESTAMP WITH TIME ZONE NULL,
    UNIQUE (aviso_id, destinatario_id)
);

-- ============================================================================
-- SEMILLA (TUPA real del instituto: tipos de pago, feriados, plantillas)
-- ============================================================================
INSERT INTO mod09.tipos_pago (codigo, nombre) VALUES
  ('TP1','Efectivo'),('TP2','Yape'),('TP3','Plin'),('TP4','Transferencia'),('TP5','Tarjeta')
ON CONFLICT (codigo) DO NOTHING;

INSERT INTO mod09.feriados (fecha, descripcion) VALUES
  ('2026-01-01','Año Nuevo'),('2026-05-01','Día del Trabajo'),
  ('2026-06-24','Día del Campesino'),('2026-07-28','Fiestas Patrias'),
  ('2026-07-29','Fiestas Patrias'),('2026-08-30','Santa Rosa'),
  ('2026-10-08','Combate de Angamos'),('2026-11-01','Todos los Santos'),
  ('2026-12-08','Inmaculada Concepción'),('2026-12-25','Navidad')
ON CONFLICT (fecha) DO NOTHING;

-- ---------------------------------------------------------------------------
-- SEMILLAS TUPA: tipos de trámite (con su concepto de pago y plazo en días
-- hábiles) y los requisitos por tipo. Datos del TUPA 2026 del IESTP Argentina.
-- ---------------------------------------------------------------------------
-- concepto_pago_id se resuelve por CÓDIGO (ids no secuenciales entre entornos)
INSERT INTO mod09.tipos_tramite (codigo, nombre, concepto_pago_id, dias_habiles)
SELECT v.codigo, v.nombre, cp.id, v.dias
FROM (VALUES
  ('TT01','Carné de Medio Pasaje',   'CP03', 3),
  ('TT02','Cambio de Turno',        'CT05', 5),
  ('TT03','Constancia de Matrícula', 'CT24', 3),
  ('TT04','Duplicado de Carné',      'CP04', 5),
  ('TA05','Traslado interno (cambio de turno)', 'CT05', 5),
  ('TA14','Reserva de matrícula',    'CT13', 5)
) AS v(codigo, nombre, concepto, dias)
JOIN mod09.conceptos_pago cp ON cp.codigo = v.concepto
ON CONFLICT (codigo) DO NOTHING;

INSERT INTO mod09.requisitos_tipos_tramite (tipo_tramite_id, orden, requisito)
SELECT tt.id, v.orden, v.requisito
FROM (VALUES
  ('TT01', 1, 'Solicitud dirigida al Director'),
  ('TT01', 2, 'Recibo de pago (CP03)'),
  ('TT01', 3, 'Foto carné fondo blanco'),
  ('TT02', 1, 'Solicitud dirigida al Director'),
  ('TT02', 2, 'Recibo de pago (CT05)'),
  ('TT02', 3, 'Record de notas'),
  ('TT03', 1, 'Solicitud dirigida al Director'),
  ('TT03', 2, 'Recibo de pago (CT24)'),
  ('TT04', 1, 'Solicitud dirigida al Director'),
  ('TT04', 2, 'Recibo de pago (CP04)'),
  ('TT04', 3, 'Foto carné fondo blanco'),
  ('TA05', 1, 'Solicitud dirigida al Director'),
  ('TA05', 2, 'Recibo de pago (CT05)'),
  ('TA05', 3, 'Carta de no adeudo de biblioteca'),
  ('TA14', 1, 'Solicitud dirigida al Director'),
  ('TA14', 2, 'Recibo de pago (CT13)'),
  ('TA14', 3, 'Carta de no adeudo de biblioteca')
) AS v(codigo, orden, requisito)
JOIN mod09.tipos_tramite tt ON tt.codigo = v.codigo
ON CONFLICT DO NOTHING;
