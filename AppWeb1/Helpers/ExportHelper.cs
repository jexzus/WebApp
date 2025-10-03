using OfficeOpenXml;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using AppWeb1.Models;
using System.Globalization;

namespace AppWeb1.Helpers
{
    public static class ExportHelper
    {
        public static byte[] GenerarExcelPedidos(List<Pedido> pedidos)
        {
            using var package = new ExcelPackage();
            var ws = package.Workbook.Worksheets.Add("Pedidos");

            ws.Cells["A1"].Value = "Nro Pedido";
            ws.Cells["B1"].Value = "Fecha";
            ws.Cells["C1"].Value = "Estado";
            ws.Cells["D1"].Value = "Detalle";
            ws.Cells["E1"].Value = "Total";

            int fila = 2;
            foreach (var pedido in pedidos)
            {
                var detalles = string.Join(", ", pedido.DetallePedidos.Select(d =>
                    $"{d.IdProductoNavigation?.NombreProducto} x{d.Cantidad}"));

                ws.Cells[fila, 1].Value = pedido.NumPedido;
                ws.Cells[fila, 2].Value = pedido.FechaPedido?.ToString("dd/MM/yyyy HH:mm");
                ws.Cells[fila, 3].Value = pedido.EstadoPedido;
                ws.Cells[fila, 4].Value = detalles;
                ws.Cells[fila, 5].Value = pedido.MontoTotal?.ToString("C", new CultureInfo("es-AR"));
                fila++;
            }

            ws.Cells[ws.Dimension.Address].AutoFitColumns();
            return package.GetAsByteArray();
        }

        public static byte[] GenerarPdfPedidos(List<Pedido> pedidos)
        {
            var stream = new MemoryStream();

            try
            {
                var document = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(30);
                        page.Header().Text("Mis Pedidos").FontSize(20).Bold().AlignCenter();
                        page.Content().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(); // Nro
                                columns.RelativeColumn(); // Fecha
                                columns.RelativeColumn(); // Estado
                                columns.RelativeColumn(); // Detalle
                                columns.RelativeColumn(); // Total
                            });

                            IContainer CellStyle(IContainer container) =>
                                container.Padding(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Nro");
                                header.Cell().Element(CellStyle).Text("Fecha");
                                header.Cell().Element(CellStyle).Text("Estado");
                                header.Cell().Element(CellStyle).Text("Detalle");
                                header.Cell().Element(CellStyle).Text("Total");
                            });

                            foreach (var p in pedidos)
                            {
                                string num = p.NumPedido.ToString();
                                string fecha = p.FechaPedido?.ToString("dd/MM/yyyy") ?? "-";
                                string estado = p.EstadoPedido ?? "-";
                                string detalle = p.DetallePedidos != null
                                    ? string.Join(", ", p.DetallePedidos.Select(d =>
                                        $"{d.IdProductoNavigation?.NombreProducto ?? "Producto"} x{d.Cantidad}"))
                                    : "-";
                                string total = p.MontoTotal?.ToString("C", new CultureInfo("es-AR")) ?? "$ 0,00";

                                table.Cell().Element(CellStyle).Text(num);
                                table.Cell().Element(CellStyle).Text(fecha);
                                table.Cell().Element(CellStyle).Text(estado);
                                table.Cell().Element(CellStyle).Text(detalle);
                                table.Cell().Element(CellStyle).Text(total);
                            }
                        });
                    });
                });

                document.GeneratePdf(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                File.WriteAllText("error_generar_pdf.txt", ex.ToString());
                Console.WriteLine("⚠ Error grave al generar PDF: " + ex.Message);
                return Array.Empty<byte>();
            }
        }
    }
}
