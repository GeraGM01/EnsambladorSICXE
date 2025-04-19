using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Practica02
{
    public enum ExprType
    {
        Absolute,
        Relative,
        Error
    }

    public struct EvalResult
    {
        public int Value;
        public ExprType Type;
        public string ErrorMsg;
        // Nueva propiedad para mantener la expresión original
        public string OriginalExpression;
    }

    /// <summary>
    /// Clase para evaluación de expresiones, determinando si son ABS o REL.
    /// </summary>
    public static class ExpressionTypeEvaluator
    {
        // Regex para identificar operadores
        private static readonly Regex OperatorRegex = new Regex(@"[\+\-\*\/]");
        private static readonly Regex ParenthesisRegex = new Regex(@"[$$$$]");

        public static EvalResult Evaluate(string expr,
                                         IReadOnlyDictionary<string, SymbolInfo> symtab,
                                         int currentAddress)
        {
            // Eliminar espacios
            expr = expr.Trim();

            // Guardar la expresión original para mostrar en listados
            string originalExpr = expr;

            // Caso trivial: "*"
            if (expr == "*")
            {
                return new EvalResult
                {
                    Value = currentAddress,
                    Type = ExprType.Relative,
                    ErrorMsg = "",
                    OriginalExpression = "*"
                };
            }

            // Intenta primero evaluar como expresión simple
            if (TryEvaluateSimple(expr, symtab, currentAddress, out EvalResult simpleResult))
            {
                simpleResult.OriginalExpression = originalExpr;
                return simpleResult;
            }

            // Si la expresión tiene operadores o paréntesis, intentamos evaluar como expresión compleja
            if (OperatorRegex.IsMatch(expr) || ParenthesisRegex.IsMatch(expr))
            {
                var result = EvaluateComplex(expr, symtab, currentAddress);
                result.OriginalExpression = originalExpr;
                return result;
            }

            // Si llegamos aquí, no pudimos evaluar la expresión
            return new EvalResult
            {
                Value = 0,
                Type = ExprType.Error,
                ErrorMsg = $"Expresión '{expr}' no soportada",
                OriginalExpression = originalExpr
            };
        }

        private static bool TryEvaluateSimple(string expr,
                                              IReadOnlyDictionary<string, SymbolInfo> symtab,
                                              int currentAddress,
                                              out EvalResult result)
        {
            // Si es un símbolo
            if (symtab.ContainsKey(expr))
            {
                var si = symtab[expr];
                result = new EvalResult
                {
                    Value = si.Address,
                    Type = si.IsRelative ? ExprType.Relative : ExprType.Absolute,
                    ErrorMsg = "",
                    OriginalExpression = expr
                };
                return true;
            }

            // Intenta parsear decimal
            if (int.TryParse(expr, out int decVal))
            {
                result = new EvalResult
                {
                    Value = decVal,
                    Type = ExprType.Absolute,
                    ErrorMsg = "",
                    OriginalExpression = expr
                };
                return true;
            }

            // Hex con sufijo H
            if (expr.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            {
                string hexPart = expr.Substring(0, expr.Length - 1);
                if (int.TryParse(hexPart, NumberStyles.HexNumber, null, out int hv))
                {
                    result = new EvalResult
                    {
                        Value = hv,
                        Type = ExprType.Absolute,
                        ErrorMsg = "",
                        OriginalExpression = expr
                    };
                    return true;
                }
            }

            // Estilo 0x
            if (expr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hx = expr.Substring(2);
                if (int.TryParse(hx, NumberStyles.HexNumber, null, out int hv2))
                {
                    result = new EvalResult
                    {
                        Value = hv2,
                        Type = ExprType.Absolute,
                        ErrorMsg = "",
                        OriginalExpression = expr
                    };
                    return true;
                }
            }

            // No se pudo evaluar como expresión simple
            result = new EvalResult();
            return false;
        }

        private static EvalResult EvaluateComplex(string expr,
                                                 IReadOnlyDictionary<string, SymbolInfo> symtab,
                                                 int currentAddress)
        {
            try
            {
                // Primero reemplazamos los símbolos por sus valores
                // y determinamos el tipo de la expresión completa
                string processedExpr = expr;
                bool hasRelativeTerms = false;
                bool hasAbsoluteTerms = false;

                // Tokenizar la expresión para identificar símbolos y operadores
                string pattern = @"[\+\-\*\/$$$$]|\w+";
                MatchCollection matches = Regex.Matches(expr, pattern);

                foreach (Match match in matches)
                {
                    string token = match.Value;

                    // Ignorar operadores y paréntesis
                    if (Regex.IsMatch(token, @"[\+\-\*\/$$$$]"))
                        continue;

                    // Verificar si el token es un símbolo en la tabla
                    if (symtab.ContainsKey(token))
                    {
                        var symbolInfo = symtab[token];
                        bool isRelative = symbolInfo.IsRelative;

                        // Actualizar flags de tipo de expresión
                        if (isRelative)
                            hasRelativeTerms = true;
                        else
                            hasAbsoluteTerms = true;

                        // Reemplazar el símbolo por su valor en la expresión
                        // Aseguramos que el reemplazo sea específico para el token completo
                        processedExpr = Regex.Replace(processedExpr,
                                                     $@"\b{Regex.Escape(token)}\b",
                                                     symbolInfo.Address.ToString());
                    }
                    else if (token == "*")
                    {
                        // Reemplazar * por la dirección actual
                        processedExpr = processedExpr.Replace("*", currentAddress.ToString());
                        hasRelativeTerms = true; // * es relativo
                    }
                    else if (!int.TryParse(token, out _) &&
                             !token.EndsWith("H", StringComparison.OrdinalIgnoreCase) &&
                             !token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        // Si no es un número, hex o símbolo conocido, es un error
                        return new EvalResult
                        {
                            Value = 0,
                            Type = ExprType.Error,
                            ErrorMsg = $"Símbolo '{token}' no encontrado en la tabla",
                            OriginalExpression = expr
                        };
                    }
                    else
                    {
                        // Es una constante numérica (absoluta)
                        hasAbsoluteTerms = true;
                    }
                }

                // Evaluamos la expresión procesada usando el evaluador de expresiones de C#
                // (seguro pero limitado a operaciones básicas)
                System.Data.DataTable table = new System.Data.DataTable();
                int result = Convert.ToInt32(table.Compute(processedExpr, ""));

                // Determinar el tipo de la expresión resultante según las reglas:
                // 1. Si solo tiene términos absolutos, es absoluta
                // 2. Si tiene términos relativos en combinaciones específicas, puede ser relativa
                // 3. Si hay términos relativos y absolutos mezclados incorrectamente, es un error

                ExprType resultType;

                if (!hasRelativeTerms)
                {
                    // Solo términos absolutos => resultado absoluto
                    resultType = ExprType.Absolute;
                }
                else if (hasRelativeTerms && !hasAbsoluteTerms)
                {
                    // Solo términos relativos pero sin operaciones entre ellos (debe ser un solo término)
                    resultType = ExprType.Relative;
                }
                else
                {
                    // Mezcla de términos relativos y absolutos
                    // En una implementación completa, analizaríamos el árbol de expresión
                    // para seguir las reglas específicas de la arquitectura SICXE

                    bool hasMultiplyOrDivide = expr.Contains("*") || expr.Contains("/");

                    if (hasMultiplyOrDivide)
                    {
                        // Hacemos una simplificación: si hay multiplicaciones o divisiones
                        // consideramos que el resultado es absoluto
                        resultType = ExprType.Absolute;
                    }
                    else
                    {
                        // Solo sumas o restas, podría ser relativo o absoluto
                        // Simplificamos y lo marcamos como absoluto para este ejemplo
                        resultType = ExprType.Absolute;
                    }
                }

                return new EvalResult
                {
                    Value = result,
                    Type = resultType,
                    ErrorMsg = "",
                    OriginalExpression = expr
                };
            }
            catch (Exception ex)
            {
                return new EvalResult
                {
                    Value = 0,
                    Type = ExprType.Error,
                    ErrorMsg = $"Error al evaluar expresión: {ex.Message}",
                    OriginalExpression = expr
                };
            }
        }
    }
}