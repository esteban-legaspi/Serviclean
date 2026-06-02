using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MySqlConnector;

namespace ServiClean
{
    public partial class MainWindow : Window
    {
        // ─── Estado ───────────────────────────────────────────────────────────
        private string _connStr = "";
        private string _currentTable = "";
        private bool _isNewRecord = false;

        // ─── Definición de tablas: (PK, es auto-increment, columnas sin PK) ──
        private readonly Dictionary<string, (string pk, bool autoPk, string[] cols)> _tables = new()
        {
            ["Domicilio"]              = ("idDomicilio",          true,  new[] { "Calle", "NumeroExterior", "NumeroInterior", "Colonia", "CodigoPostal", "Municipio", "Estado" }),
            ["Empleado"]               = ("idEmpleado",           true,  new[] { "Nombre", "salarioDiario", "rfc", "curp", "fechaIngreso", "fechaBaja", "nss", "idDomicilio" }),
            ["cliente"]                = ("idCliente",            true,  new[] { "rfc_cliente", "nombre_empresa" }),
            ["contratoServicio"]       = ("id_contrato",          true,  new[] { "idCliente", "fecha_inicio", "fecha_termino", "observaciones", "costo_mensual_sin_iva", "numero_personas_asignadas" }),
            ["AuxiliardeLimpieza"]     = ("idEmpleado",           false, new[] { "id_Contrato" }),
            ["nomina"]                 = ("id_nomina",            true,  new[] { "periodo", "fecha_calculo" }),
            ["detalleNomina"]          = ("id_detalle",           true,  new[] { "idEmpleado", "id_nomina", "vacaciones", "deduccion_consufin", "horas_extra", "dias_trabajados", "sueldo_total", "deduccion_ahorro", "deduccion_infonavit", "deduccion_fonacot" }),
            ["Incidencia"]             = ("id_incidencia",        true,  new[] { "idEmpleado", "id_detalle", "fecha", "justificada", "tipo" }),
            ["contactoCliente"]        = ("idContacto",           true,  new[] { "clienteidCliente", "contacto_nombre", "contacto_email", "contacto_telefono" }),
            ["factura"]                = ("id_factura",           true,  new[] { "idCliente", "folio", "fecha_emision", "subtotal", "retencion_isn", "fechaPago" }),
            ["Prospecto"]              = ("idProspecto",          true,  new[] { "Nombre", "Telefono", "idContrato", "idDomicilio" }),
            ["Comodin"]                = ("idEmpleado",           false, new string[] { }),
            ["Comodin_contratoServicio"] = ("idEmpleado",         false, new[] { "id_contrato" }),
            ["prestamoEmpleado"]       = ("id_prestamo",          true,  new[] { "EmpleadoidEmpleado", "monto_total", "descuento_semanal", "fecha_otorgamiento" }),
            ["ahorroEmpleado"]         = ("id_ahorro",            true,  new[] { "EmpleadoidEmpleado", "semana", "cantidad_ahorrada", "saldo_acumulado" }),
        };

        // ─── Nombre visible → nombre real en BD ──────────────────────────────
        private readonly Dictionary<string, string> _tableMap = new()
        {
            ["Domicilio"]          = "Domicilio",
            ["Empleado"]           = "Empleado",
            ["Cliente"]            = "cliente",
            ["Contrato Servicio"]  = "contratoServicio",
            ["Auxiliar Limpieza"]  = "AuxiliardeLimpieza",
            ["Nomina"]             = "nomina",
            ["Detalle Nomina"]     = "detalleNomina",
            ["Incidencia"]         = "Incidencia",
            ["Contacto Cliente"]   = "contactoCliente",
            ["Factura"]            = "factura",
            ["Prospecto"]          = "Prospecto",
            ["Comodin"]            = "Comodin",
            ["Comodin Contrato"]   = "Comodin_contratoServicio",
            ["Prestamo Empleado"]  = "prestamoEmpleado",
            ["Ahorro Empleado"]    = "ahorroEmpleado",
        };

        // ─── Consultas ────────────────────────────────────────────────────────
        // Las fechas están fijas al periodo de datos de prueba.
        // En producción reemplazar con CURDATE(), MONTH(CURDATE()), etc.
        private readonly Dictionary<string, string> _queries = new()
        {
            ["1. Nomina x empleado"] =
                "SELECT e.Nombre, dn.sueldo_total, n.periodo " +
                "FROM Empleado e " +
                "JOIN detalleNomina dn ON dn.idEmpleado = e.idEmpleado " +
                "JOIN nomina n ON n.id_nomina = dn.id_nomina " +
                "WHERE n.periodo = '2026-MAY-SEM2'",                          // produccion: periodo dinamico

            ["2. Nomina x empresa"] =
                "SELECT c.nombre_empresa, n.periodo, SUM(dn.sueldo_total) AS totalNomina " +
                "FROM cliente c " +
                "JOIN contratoServicio cs ON cs.idCliente = c.idCliente " +
                "JOIN AuxiliardeLimpieza al ON al.id_Contrato = cs.id_contrato " +
                "JOIN detalleNomina dn ON dn.idEmpleado = al.idEmpleado " +
                "JOIN nomina n ON n.id_nomina = dn.id_nomina " +
                "WHERE n.periodo = '2026-MAY-SEM2' " +                       // produccion: periodo dinamico
                "GROUP BY c.nombre_empresa, n.periodo",

            ["3. Cuentas x cobrar"] =
                "SELECT c.nombre_empresa, SUM(f.subtotal) AS totalPorCobrar " +
                "FROM cliente c " +
                "JOIN factura f ON f.idCliente = c.idCliente " +
                "WHERE f.fechaPago IS NULL " +
                "GROUP BY c.nombre_empresa",

            ["4. Incidencias diarias"] =
                "SELECT e.Nombre, c.nombre_empresa, i.fecha, i.justificada, i.tipo " +
                "FROM Incidencia i " +
                "JOIN Empleado e ON i.idEmpleado = e.idEmpleado " +
                "JOIN AuxiliardeLimpieza a ON a.idEmpleado = e.idEmpleado " +
                "JOIN contratoServicio cs ON cs.id_contrato = a.id_Contrato " +
                "JOIN cliente c ON c.idCliente = cs.idCliente " +
                "WHERE i.fecha = '2026-05-28'",                               // produccion: CURDATE()

            ["5. Incidencias semanales"] =
                "SELECT e.Nombre, c.nombre_empresa, COUNT(i.tipo) AS incidenciasSemanales " +
                "FROM cliente c " +
                "JOIN contratoServicio cs ON cs.idCliente = c.idCliente " +
                "JOIN AuxiliardeLimpieza al ON al.id_Contrato = cs.id_contrato " +
                "JOIN Empleado e ON e.idEmpleado = al.idEmpleado " +
                "JOIN Incidencia i ON i.idEmpleado = e.idEmpleado " +
                "WHERE i.fecha BETWEEN '2026-05-25' AND '2026-05-31' " +     // produccion: semana dinamica
                "GROUP BY e.Nombre, c.nombre_empresa",

            ["6. Incidencias mensuales"] =
                "SELECT e.Nombre, c.nombre_empresa, COUNT(i.tipo) AS incidenciasMensuales " +
                "FROM cliente c " +
                "JOIN contratoServicio cs ON cs.idCliente = c.idCliente " +
                "JOIN AuxiliardeLimpieza al ON al.id_Contrato = cs.id_contrato " +
                "JOIN Empleado e ON e.idEmpleado = al.idEmpleado " +
                "JOIN Incidencia i ON i.idEmpleado = e.idEmpleado " +
                "WHERE MONTH(i.fecha) = 5 AND YEAR(i.fecha) = 2026 " +       // produccion: MONTH/YEAR(CURDATE())
                "GROUP BY e.Nombre, c.nombre_empresa",

            ["7. Altas y Bajas IMSS"] =
                "SELECT Nombre, nss, curp, salarioDiario, FechaIngreso, fechaBaja " +
                "FROM Empleado " +
                "WHERE MONTH(FechaIngreso) = 5 OR MONTH(fechaBaja) = 5",      // produccion: MONTH(CURDATE())

            ["8. Contratos x cliente"] =
                "SELECT c.nombre_empresa, cs.fecha_inicio, cs.fecha_termino, cs.costo_mensual_sin_iva " +
                "FROM cliente c " +
                "JOIN contratoServicio cs ON cs.idCliente = c.idCliente",

            ["9. Empleados x empresa"] =
                "SELECT c.nombre_empresa, e.Nombre, e.salarioDiario " +
                "FROM cliente c " +
                "JOIN contratoServicio cs ON cs.idCliente = c.idCliente " +
                "JOIN AuxiliardeLimpieza al ON al.id_Contrato = cs.id_contrato " +
                "JOIN Empleado e ON e.idEmpleado = al.idEmpleado",

            ["10. Prospectos"] =
                "SELECT p.Nombre, p.Telefono, c.nombre_empresa " +
                "FROM Prospecto p " +
                "JOIN contratoServicio cs ON cs.id_contrato = p.idContrato " +
                "JOIN cliente c ON c.idCliente = cs.idCliente",

            ["11. Incidencias recurrentes"] =
                "SELECT e.Nombre, COUNT(i.id_incidencia) AS total_incidencias " +
                "FROM Empleado e " +
                "JOIN Incidencia i ON i.idEmpleado = e.idEmpleado " +
                "WHERE MONTH(i.fecha) = 5 AND YEAR(i.fecha) = 2026 " +        // produccion: MONTH/YEAR(CURDATE())
                "GROUP BY e.Nombre " +
                "HAVING COUNT(i.id_incidencia) > 1",
        };

        // ─── Constructor ──────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
        }

        // ─── Conexión ─────────────────────────────────────────────────────────
        private void BtnConectar_Click(object sender, RoutedEventArgs e)
        {
            _connStr = $"Server={txtServer.Text};Port={txtPort.Text};" +
                       $"Database={txtDatabase.Text};Uid={txtUser.Text};Pwd={txtPassword.Text};";
            try
            {
                using var conn = new MySqlConnection(_connStr);
                conn.Open();
                lblStatus.Text = "✔ Conectado";
                lblStatus.Foreground = Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "✘ " + ex.Message;
                lblStatus.Foreground = Brushes.Tomato;
            }
        }

        // ─── Selección de tabla ───────────────────────────────────────────────
        private void LstTablas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstTablas.SelectedItem is not ListBoxItem item) return;
            lstConsultas.SelectedItem = null;

            string display = item.Content.ToString()!;
            if (!_tableMap.TryGetValue(display, out string? table)) return;

            _currentTable = table;
            lblFormTitle.Text = $"Tabla: {display}";
            LoadTable(table);
            BuildForm(table);
            pnlForm.Visibility = Visibility.Visible;
        }

        // ─── Selección de consulta ────────────────────────────────────────────
        private void LstConsultas_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstConsultas.SelectedItem is not ListBoxItem item) return;
            lstTablas.SelectedItem = null;
            pnlForm.Visibility = Visibility.Collapsed;
            _currentTable = "";

            string name = item.Content.ToString()!;
            if (_queries.TryGetValue(name, out string? sql))
                ExecuteQuery(sql);
        }

        // ─── Cargar tabla completa ─────────────────────────────────────────────
        private void LoadTable(string tableName) =>
            ExecuteQuery($"SELECT * FROM `{tableName}`");

        // ─── Ejecutar cualquier SELECT ────────────────────────────────────────
        private void ExecuteQuery(string sql)
        {
            if (string.IsNullOrEmpty(_connStr))
            {
                MessageBox.Show("Primero conéctate al servidor.", "Sin conexión");
                return;
            }
            try
            {
                using var conn = new MySqlConnection(_connStr);
                conn.Open();
                var adapter = new MySqlDataAdapter(sql, conn);
                var dt = new DataTable();
                adapter.Fill(dt);
                dgData.ItemsSource = dt.DefaultView;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al ejecutar consulta:\n" + ex.Message, "Error");
            }
        }

        // ─── Construir formulario dinámico ────────────────────────────────────
        private void BuildForm(string tableName)
        {
            wpForm.Children.Clear();
            if (!_tables.TryGetValue(tableName, out var def)) return;

            // Campo PK (siempre primero, siempre read-only)
            AddField(def.pk, "🔑 " + def.pk, readOnly: true);

            // Campos editables
            foreach (var col in def.cols)
                AddField(col, col, readOnly: false);
        }

        private void AddField(string col, string label, bool readOnly)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 10, 0), Width = 140 };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            sp.Children.Add(new TextBox
            {
                Tag = col,
                IsReadOnly = readOnly,
                Background = new SolidColorBrush(readOnly
                    ? Color.FromRgb(49, 50, 68)
                    : Color.FromRgb(39, 39, 58)),
                Foreground = new SolidColorBrush(readOnly
                    ? Color.FromRgb(108, 112, 134)
                    : Color.FromRgb(205, 214, 244)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                Padding = new Thickness(4, 2, 4, 2),
                FontSize = 12
            });
            wpForm.Children.Add(sp);
        }

        // ─── Fila seleccionada → llenar formulario ────────────────────────────
        private void DgData_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgData.SelectedItem is not DataRowView row) return;
            _isNewRecord = false;

            foreach (StackPanel sp in wpForm.Children)
            {
                if (sp.Children[1] is TextBox tb && tb.Tag is string col)
                {
                    try { tb.Text = row[col]?.ToString() ?? ""; }
                    catch { tb.Text = ""; }
                }
            }
        }

        // ─── Nuevo registro ───────────────────────────────────────────────────
        private void BtnNuevo_Click(object sender, RoutedEventArgs e)
        {
            _isNewRecord = true;
            foreach (StackPanel sp in wpForm.Children)
                if (sp.Children[1] is TextBox tb)
                    tb.Text = "";
        }

        // ─── Guardar (INSERT o UPDATE) ────────────────────────────────────────
        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentTable)) return;
            if (!_tables.TryGetValue(_currentTable, out var def)) return;

            // Recoger valores del formulario
            var values = new Dictionary<string, string>();
            foreach (StackPanel sp in wpForm.Children)
                if (sp.Children[1] is TextBox tb && tb.Tag is string col)
                    values[col] = tb.Text.Trim();

            try
            {
                using var conn = new MySqlConnection(_connStr);
                conn.Open();
                MySqlCommand cmd;

                if (_isNewRecord)
                {
                    // INSERT
                    var insertCols = new List<string>();
                    var insertParams = new List<string>();

                    // PK manual (no auto-increment)
                    if (!def.autoPk && values.TryGetValue(def.pk, out var pkVal) && !string.IsNullOrEmpty(pkVal))
                    {
                        insertCols.Add($"`{def.pk}`");
                        insertParams.Add($"@{def.pk}");
                    }

                    foreach (var col in def.cols)
                    {
                        insertCols.Add($"`{col}`");
                        insertParams.Add($"@{col}");
                    }

                    string sql = $"INSERT INTO `{_currentTable}` ({string.Join(", ", insertCols)}) VALUES ({string.Join(", ", insertParams)})";
                    cmd = new MySqlCommand(sql, conn);

                    if (!def.autoPk && values.ContainsKey(def.pk))
                        cmd.Parameters.AddWithValue($"@{def.pk}", NullIfEmpty(values[def.pk]));

                    foreach (var col in def.cols)
                        cmd.Parameters.AddWithValue($"@{col}", values.TryGetValue(col, out var v) ? NullIfEmpty(v) : DBNull.Value);
                }
                else
                {
                    // UPDATE
                    if (!values.TryGetValue(def.pk, out var pkVal) || string.IsNullOrEmpty(pkVal))
                    {
                        MessageBox.Show("Selecciona un registro para editar.", "Aviso");
                        return;
                    }

                    if (def.cols.Length == 0)
                    {
                        MessageBox.Show("Esta tabla no tiene campos editables.", "Aviso");
                        return;
                    }

                    var setClauses = def.cols.Select(c => $"`{c}` = @{c}");
                    string sql = $"UPDATE `{_currentTable}` SET {string.Join(", ", setClauses)} WHERE `{def.pk}` = @pk";
                    cmd = new MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@pk", pkVal);

                    foreach (var col in def.cols)
                        cmd.Parameters.AddWithValue($"@{col}", values.TryGetValue(col, out var v) ? NullIfEmpty(v) : DBNull.Value);
                }

                cmd.ExecuteNonQuery();
                LoadTable(_currentTable);
                MessageBox.Show(_isNewRecord ? "Registro insertado correctamente." : "Registro actualizado correctamente.", "Éxito");
                _isNewRecord = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar:\n" + ex.Message, "Error");
            }
        }

        // ─── Eliminar ─────────────────────────────────────────────────────────
        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentTable)) return;
            if (!_tables.TryGetValue(_currentTable, out var def)) return;
            if (dgData.SelectedItem is not DataRowView row)
            {
                MessageBox.Show("Selecciona un registro para eliminar.", "Aviso");
                return;
            }

            if (MessageBox.Show("¿Eliminar este registro?", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                using var conn = new MySqlConnection(_connStr);
                conn.Open();
                var cmd = new MySqlCommand($"DELETE FROM `{_currentTable}` WHERE `{def.pk}` = @pk", conn);
                cmd.Parameters.AddWithValue("@pk", row[def.pk]);
                cmd.ExecuteNonQuery();
                LoadTable(_currentTable);
                MessageBox.Show("Registro eliminado.", "Éxito");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al eliminar:\n" + ex.Message, "Error");
            }
        }

        // ─── Recargar tabla ───────────────────────────────────────────────────
        private void BtnRecargar_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentTable))
                LoadTable(_currentTable);
        }

        // ─── Utilidad: vacío → NULL ───────────────────────────────────────────
        private static object NullIfEmpty(string val) =>
            string.IsNullOrEmpty(val) ? DBNull.Value : (object)val;
    }
}
