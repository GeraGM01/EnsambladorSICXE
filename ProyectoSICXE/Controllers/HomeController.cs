using Antlr.Runtime;
using Antlr.Runtime.Tree;
using Microsoft.AspNetCore.Mvc;
using Practica02;
using ProyectoSICXE.Models;
using System.Diagnostics;

namespace ProyectoSICXE.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private static Pass1Visitor _ultimoPaso1;  //auxiliar que ayuda a que no se pierdan datos entre solicitudes

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }


        [HttpPost]
        public IActionResult EjecutarPaso1([FromBody] string entradaDeCodigo)
        {
            try
            {
                // 2) Crear lexer y parser
                ANTLRStringStream input = new ANTLRStringStream(entradaDeCodigo);
                Gram_SICXELexer lexer = new Gram_SICXELexer(input);
                CommonTokenStream tokens = new CommonTokenStream(lexer);
                Gram_SICXEParser parser = new Gram_SICXEParser(tokens);

                // Crear el visitor para el Paso 1
                Pass1Visitor pass1 = new Pass1Visitor();
                Gram_SICXEParser.Pass1Ref = pass1;

                // Ejecutamos parser.programa() para obtener el AST
                var resultado = parser.programa();
                CommonTree ast = (CommonTree)resultado.Tree;

                // Recorremos el AST con Pass1Visitor
                pass1.Paso1(ast);
                _ultimoPaso1 = pass1; //indico que ya se hizo completamente el paso1

                // Aqui creo el objeto anonimo para retornar los datos que voy a ocupar solamente
                var response = new
                {
                    Success = true,
                    ArbolAST = ast.ToStringTree(),
                    ListadoIntermedio = pass1.Lines.Select(line => new
                    {
                        Address = line.Address.ToString("X4"),
                        BlockNumber = line.BlockNumber,
                        Label = line.Label ?? "",
                        Mnemonic = line.Mnemonic ?? "",
                        Operand = line.Operand ?? "",
                        Format = line.Format ?? "",
                        Error = line.Error
                    }).ToList(),
                    FinalLocctr = pass1.FinalLocctr.ToString("X4"),
                    SymbolTable = pass1.SymbolInfoTable.Select(kvp => new
                    {
                        Symbol = kvp.Key,
                        Address = kvp.Value.Address.ToString("X6"),
                        Type = kvp.Value.IsRelative ? "REL" : "ABS",
                        BlockNumber = kvp.Value.BlockNumber
                    }).ToList(),
                    BlockTable = pass1.BlockTable.Select(kvp => new
                    {
                        Number = kvp.Key,
                        Name = string.IsNullOrEmpty(kvp.Value.Name) ? "(por omisión)" : kvp.Value.Name,
                        StartAddress = kvp.Value.StartAddress.ToString("X6"),
                        Length = kvp.Value.Length.ToString("X6")
                    }).ToList(),
                    ErroresLexicosSintacticos = pass1.errores,
                    ErroresSemanticos = pass1.SemanticErrors
                };

                return Json(response);
            }
            catch (RecognitionException re)
            {
                return Json(new { Success = false, Error = "Error de parseo: " + re.Message });
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Error = "Error general: " + ex.Message });
            }
        }

        [HttpPost]
        public IActionResult EjecutarPaso2()
        {
            try
            {
                if (_ultimoPaso1 == null)
                {
                    return Json(new { Success = false, Error = "Necesitas ejecutar el Paso 1 primero." });
                }

                // Ejecutar el Paso 2
                Pass2Visitor pass2 = new Pass2Visitor();
                pass2.Paso2(_ultimoPaso1);

                // Generar registros H, T, M, E
                pass2.GenerarRegistrosHTME(_ultimoPaso1);

                // Crear objeto de respuesta
                var response = new
                {
                    Success = true,
                    ListadoIntermedioObjCode = _ultimoPaso1.Lines.Select(line => new
                    {
                        Address = line.Address.ToString("X4"),
                        Label = line.Label ?? "",
                        Mnemonic = line.Mnemonic ?? "",
                        Operand = (line.Mnemonic?.Equals("EQU", StringComparison.OrdinalIgnoreCase) == true ||
                                   line.Mnemonic?.Equals("WORD", StringComparison.OrdinalIgnoreCase) == true) &&
                                  !string.IsNullOrEmpty(line.OriginalExpression) ?
                                  line.OriginalExpression : line.Operand ?? "",
                        Format = line.Format ?? "",
                        Error = line.Error,
                        ObjectCode = line.ObjectCode ?? "",
                        IsRelocatable = line.IsRelocatable
                    }).ToList(),
                    RegistrosHTME = pass2.ObjRecords
                };

                return Json(response);
            }
            catch (Exception ex)
            {
                return Json(new { Success = false, Error = "Error en el Paso 2: " + ex.Message });
            }
        }

    }
}
