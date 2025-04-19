using System;
using System.IO;
using Antlr.Runtime;
using Practica02;

public partial class Gram_SICXEParser
{
    // Se asigna desde Program.cs
    public static Pass1Visitor Pass1Ref = null;

    private static string logFile = @"C:\Users\goku0\Downloads\errores.txt";

    public override void DisplayRecognitionError(string[] tokenNames, RecognitionException e)
    {
        string errorTipo = (e is MismatchedTokenException) ? "Error Sintactico" : "Error Lexico";
        int lineaError = e.Line;

        // Línea original (o fallback si no existe)
        string contenidoLinea = GetLineTextFromOriginal(lineaError);

        string mensaje = $"Error en línea {lineaError}: {errorTipo}( {contenidoLinea} ) Instrucción No Existe";

        if (Pass1Ref != null)
        {
            // 1) Guardar en la lista "errores" (texto plano).
            Pass1Ref.errores.Add(mensaje);

            // 2) Guardar en la lista de objetos ErrorLex para inyección posterior
            Pass1Ref.LexicalErrors.Add(new ErrorLex
            {
                LineNumber = lineaError,
                RawLine = contenidoLinea,
                ErrorMsg = "Instrucción No Existe"
            });
        }

        Console.WriteLine(mensaje);

        // (Opcional) Guardar inmediatamente en logFile
        try
        {
            File.AppendAllText(logFile, mensaje + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine("No se pudo guardar en el archivo de errores: " + ex.Message);
        }
    }

    private string GetLineTextFromOriginal(int line)
    {
        if (!string.IsNullOrEmpty(ParserUtil.OriginalText))
        {
            string[] lines = ParserUtil.OriginalText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (line - 1 < lines.Length)
                return lines[line - 1];
            else
                return ParserUtil.OriginalText;
        }
        return "";
    }
}
