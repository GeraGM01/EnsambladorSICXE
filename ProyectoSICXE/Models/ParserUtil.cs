using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Practica02
{
    public static class ParserUtil
    {
        public static string OriginalText { get; set; }
    }
    // Estructura simple para almacenar datos del error léx/sint
    public class ErrorLex
    {
        public int LineNumber { get; set; }  // línea donde ocurrió
        public string RawLine { get; set; }  // texto original de esa línea
        public string ErrorMsg { get; set; }  // mensaje (p.ej. "Instrucción No Existe")
    }
}
