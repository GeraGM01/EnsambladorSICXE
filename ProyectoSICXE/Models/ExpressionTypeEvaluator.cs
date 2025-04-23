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
        public string OriginalExpression;
    }

    public static class ExpressionTypeEvaluator
    {
        private static readonly Regex OperatorRegex = new Regex(@"[\+\-\*\/]");
        private static readonly Regex ParenthesisRegex = new Regex(@"[\(\)]");

        /// <summary>
        /// Paso1: Evalúa la expresión sabiendo que cada símbolo relativo está en 'symtab' con su offset 'Address'
        /// y su 'BlockNumber'.
        /// 
        /// "isEqu" = true => Se prohíbe mezclar símbolos de distintos bloques, aunque se anulen,
        ///                  y se desea forzar error en ese caso.
        /// </summary>
        public static EvalResult Evaluate(
            string expr,
            IReadOnlyDictionary<string, SymbolInfo> symtab,
            int currentAddress,
            int currentBlockNumber,
            bool isEqu = false
        )
        {
            return EvaluateInternal(expr, symtab, currentAddress, currentBlockNumber, isEqu);
        }

        private static EvalResult EvaluateInternal(
            string expr,
            IReadOnlyDictionary<string, SymbolInfo> symtab,
            int currentAddress,
            int currentBlockNumber,
            bool isEqu
        )
        {
            expr = expr.Trim();
            string originalExpr = expr;

            // Manejo de "*" (un solo token)
            if (expr == "*")
            {
                // '*' => relativo
                return new EvalResult
                {
                    Value = currentAddress,
                    Type = ExprType.Relative,
                    ErrorMsg = "",
                    OriginalExpression = "*"
                };
            }

            // Intento de interpretación rápida (un solo token sin operadores)
            if (TryEvaluateSimple(expr, symtab, currentAddress, currentBlockNumber, out EvalResult sres))
            {
                sres.OriginalExpression = originalExpr;
                return sres;
            }

            // Si contiene operadores => EvaluateComplex
            if (OperatorRegex.IsMatch(expr) || ParenthesisRegex.IsMatch(expr))
            {
                var complex = EvaluateComplex(expr, symtab, currentAddress, currentBlockNumber, isEqu);
                complex.OriginalExpression = originalExpr;
                return complex;
            }

            // De lo contrario => error
            return new EvalResult
            {
                Value = 0,
                Type = ExprType.Error,
                ErrorMsg = $"Expresión '{expr}' no soportada",
                OriginalExpression = expr
            };
        }

        private static EvalResult EvaluateComplex(
            string expr,
            IReadOnlyDictionary<string, SymbolInfo> symtab,
            int currentAddress,
            int currentBlockNumber,
            bool isEqu
        )
        {
            try
            {
                Dictionary<int, int> blockSignCount = new Dictionary<int, int>();
                bool foundRelative = false;
                bool foundMulDiv = false;
                int currentSign = +1;

                List<string> numericTokens = new List<string>();
                string pattern = @"(\+|\-|\*|/|\(|\)|0x[0-9A-Fa-f]+|[0-9A-Fa-f]+[Hh]|\d+|\w+)";
                var matches = Regex.Matches(expr, pattern);

                foreach (Match m in matches)
                {
                    string tk = m.Value;

                    if (tk == "+" || tk == "-")
                    {
                        numericTokens.Add(tk);
                        currentSign = (tk == "+") ? +1 : -1;
                    }
                    else if (tk == "*" || tk == "/")
                    {
                        foundMulDiv = true;
                        numericTokens.Add(tk);
                        currentSign = +1;
                    }
                    else if (tk == "(" || tk == ")")
                    {
                        numericTokens.Add(tk);
                    }
                    else if (EsNumeroHexOdecimal(tk, out int valorNumerico))
                    {
                        numericTokens.Add(valorNumerico.ToString());
                        currentSign = +1;
                    }
                    else if (tk == "*")
                    {
                        foundRelative = true;
                        if (!blockSignCount.ContainsKey(currentBlockNumber))
                            blockSignCount[currentBlockNumber] = 0;
                        blockSignCount[currentBlockNumber] += currentSign;

                        numericTokens.Add(currentAddress.ToString());
                        currentSign = +1;
                    }
                    else
                    {
                        // Símbolo
                        if (!symtab.ContainsKey(tk))
                        {
                            return new EvalResult
                            {
                                Value = 0,
                                Type = ExprType.Error,
                                ErrorMsg = $"Símbolo '{tk}' no encontrado.",
                                OriginalExpression = expr
                            };
                        }
                        var si = symtab[tk];

                        if (si.IsRelative)
                        {
                            foundRelative = true;
                            if (!blockSignCount.ContainsKey(si.BlockNumber))
                                blockSignCount[si.BlockNumber] = 0;
                            blockSignCount[si.BlockNumber] += currentSign;
                        }

                        numericTokens.Add(si.Address.ToString());
                        currentSign = +1;
                    }
                }

                // Regla: no se permiten términos relativos en * / 
                if (foundRelative && foundMulDiv)
                {
                    return new EvalResult
                    {
                        Value = 0,
                        Type = ExprType.Error,
                        ErrorMsg = "No se permiten términos relativos en * o /",
                        OriginalExpression = expr
                    };
                }

                int blocksNonZero = 0;
                int leftoverBlock = -1, leftoverValue = 0;
                foreach (var kv in blockSignCount)
                {
                    if (kv.Value != 0)
                    {
                        blocksNonZero++;
                        leftoverBlock = kv.Key;
                        leftoverValue = kv.Value;
                    }
                }

                // Si es EQU y blockSignCount.Count > 1 => error
                if (isEqu && blockSignCount.Count > 1)
                {
                    return new EvalResult
                    {
                        Value = 0,
                        Type = ExprType.Error,
                        ErrorMsg = "Expresion Invalida",
                        OriginalExpression = expr
                    };
                }

                // Caso 1: blocksNonZero == 0 => Absoluto
                if (blocksNonZero == 0)
                {
                    string exprNumeric = string.Join(" ", numericTokens);
                    int valAbs = EvaluateByDataTable(exprNumeric);
                    return new EvalResult
                    {
                        Value = valAbs,
                        Type = ExprType.Absolute,
                        ErrorMsg = "",
                        OriginalExpression = expr
                    };
                }
                // Caso 2: blocksNonZero == 1 => cheque leftoverValue
                else if (blocksNonZero == 1)
                {
                    if (leftoverValue == +1 || leftoverValue == -1)
                    {
                        string exprNumeric = string.Join(" ", numericTokens);
                        int valRel = EvaluateByDataTable(exprNumeric);
                        return new EvalResult
                        {
                            Value = valRel,
                            Type = ExprType.Relative,
                            ErrorMsg = "",
                            OriginalExpression = expr
                        };
                    }
                    else
                    {
                        return new EvalResult
                        {
                            Value = 0,
                            Type = ExprType.Error,
                            ErrorMsg = $"Saldo relativo {leftoverValue} en bloque {leftoverBlock}, no válido",
                            OriginalExpression = expr
                        };
                    }
                }
                else
                {
                    // 2+ bloques con saldo != 0
                    return new EvalResult
                    {
                        Value = 0,
                        Type = ExprType.Error,
                        ErrorMsg = "Se mezclan símbolos de múltiples bloques sin anularse.",
                        OriginalExpression = expr
                    };
                }
            }
            catch (Exception ex)
            {
                return new EvalResult
                {
                    Value = 0,
                    Type = ExprType.Error,
                    ErrorMsg = "Error EvaluateComplex => " + ex.Message,
                    OriginalExpression = expr
                };
            }
        }

        private static bool EsNumeroHexOdecimal(string tk, out int valor)
        {
            valor = 0;
            if (int.TryParse(tk, NumberStyles.Integer, CultureInfo.InvariantCulture, out valor))
                return true;

            if (tk.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hx = tk.Substring(2);
                if (int.TryParse(hx, NumberStyles.HexNumber, null, out valor))
                    return true;
                return false;
            }

            if (tk.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            {
                string hexPart = tk.Substring(0, tk.Length - 1);
                if (int.TryParse(hexPart, NumberStyles.HexNumber, null, out valor))
                    return true;
                return false;
            }
            return false;
        }

        private static bool TryEvaluateSimple(
            string expr,
            IReadOnlyDictionary<string, SymbolInfo> symtab,
            int currentAddress,
            int currentBlockNumber,
            out EvalResult result)
        {
            // ¿Símbolo?
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

            // decimal
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

            // Hex sufijo H
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

            // Hex prefijo 0x
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

            result = default(EvalResult);
            return false;
        }

        private static int EvaluateByDataTable(string rawExpr)
        {
            try
            {
                var table = new System.Data.DataTable();
                int val = Convert.ToInt32(table.Compute(rawExpr, ""));
                return val;
            }
            catch
            {
                return 0;
            }
        }
    }
}
